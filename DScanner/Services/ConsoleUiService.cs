using DScanner.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DScanner.Services;

public sealed class ConsoleUiService(
    IOptions<ScannerOptions> options,
    ILogger<ConsoleUiService> logger)
    : BackgroundService, IConsoleUi
{
    private const int TitleRow = 0;
    private const int StatusRow = 1;
    private const int LoaderRow = 2;
    private const int EventsHeaderRow = 3;
    private const int EventsStartRow = 4;
    private const string Footer = "Press Ctrl+Q to quit";

    private readonly object _gate = new();
    private readonly Queue<ConsoleEvent> _events = [];
    private readonly ScannerOptions _options = options.Value;
    private string _status = "Starting USB device watchers...";
    private string _loader = "Enumeration: waiting";
    private int _progressDots;
    private int _lastWidth;
    private int _lastHeight;
    private bool _renderFailureLogged;

    private bool IsInteractive => !Console.IsOutputRedirected;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        RenderLayout();

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
            RedrawIfResized();
        }
    }

    public void SetStatus(string status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(status);

        lock (_gate)
        {
            _status = status;
            RenderLine(StatusRow, _status, ConsoleColor.Gray);
            RenderFooter();
        }
    }

    public void BeginEnumeration()
    {
        lock (_gate)
        {
            _progressDots = 0;
            _loader = "Enumeration: scanning";
            RenderLine(LoaderRow, _loader, ConsoleColor.Yellow);
            RenderFooter();
        }
    }

    public void AdvanceEnumeration()
    {
        lock (_gate)
        {
            _progressDots++;
            _loader = $"Enumeration: scanning{new string('.', _progressDots)}";
            RenderLine(LoaderRow, _loader, ConsoleColor.Yellow);
            RenderFooter();
        }
    }

    public void EndEnumeration(TimeSpan elapsed)
    {
        lock (_gate)
        {
            _loader = $"Enumeration: completed in {elapsed.TotalSeconds:F1}s";
            RenderLine(LoaderRow, _loader, ConsoleColor.Green);
            RenderFooter();
        }
    }

    public void AddEvent(
        string message,
        ConsoleColor color = ConsoleColor.White)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        lock (_gate)
        {
            _events.Enqueue(
                new ConsoleEvent(
                    [new ConsoleSegment(
                        $"{DateTime.Now:HH:mm:ss.fff}  {message}",
                        color)]));
            TrimEvents();
            RenderEvents();
            RenderFooter();
        }
    }

    public void AddHighlightedEvent(
        string highlightedText,
        string remainingText,
        ConsoleColor highlightColor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(highlightedText);
        ArgumentNullException.ThrowIfNull(remainingText);

        lock (_gate)
        {
            _events.Enqueue(
                new ConsoleEvent(
                    [
                        new ConsoleSegment(
                            $"{DateTime.Now:HH:mm:ss.fff}  ",
                            ConsoleColor.White),
                        new ConsoleSegment(highlightedText, highlightColor),
                        new ConsoleSegment(remainingText, ConsoleColor.White)
                    ]));
            TrimEvents();
            RenderEvents();
            RenderFooter();
        }
    }

    private void RenderLayout()
    {
        if (!TryGetDimensions(out int width, out int height))
        {
            return;
        }

        lock (_gate)
        {
            TryRender(() =>
            {
                Console.Clear();
                _lastWidth = width;
                _lastHeight = height;
                RenderLine(
                    TitleRow,
                    "DScanner - DirectInput Controller Scanner",
                    ConsoleColor.Cyan,
                    center: true);
                RenderLine(
                    StatusRow,
                    $"USB DirectInput devices | {_options.PollFrequencyHz} Hz | Fast enumeration",
                    ConsoleColor.Gray);
                RenderLine(LoaderRow, _loader, ConsoleColor.Yellow);
                RenderLine(EventsHeaderRow, "Input and USB events", ConsoleColor.DarkCyan);
                RenderEvents();
                RenderFooter();
            });
        }
    }

    private void RedrawIfResized()
    {
        if (!TryGetDimensions(out int width, out int height)
            || (width == _lastWidth && height == _lastHeight))
        {
            return;
        }

        RenderLayout();
    }

    private void RenderEvents()
    {
        if (!TryGetDimensions(out _, out int height))
        {
            return;
        }

        int footerRow = height - 1;
        int eventRowCount = Math.Max(footerRow - EventsStartRow, 0);
        TrimEvents(eventRowCount);
        ConsoleEvent[] events = _events.ToArray();

        for (int offset = 0; offset < eventRowCount; offset++)
        {
            if (offset < events.Length)
            {
                RenderEventLine(EventsStartRow + offset, events[offset]);
            }
            else
            {
                RenderLine(
                    EventsStartRow + offset,
                    string.Empty,
                    ConsoleColor.White);
            }
        }
    }

    private void RenderEventLine(int row, ConsoleEvent consoleEvent)
    {
        if (!TryGetDimensions(out int width, out int height)
            || row < 0
            || row >= height)
        {
            return;
        }

        TryRender(() =>
        {
            int remainingWidth = Math.Max(width - 1, 1);
            Console.SetCursorPosition(0, row);
            Console.BackgroundColor = ConsoleColor.Black;

            foreach (ConsoleSegment segment in consoleEvent.Segments)
            {
                if (remainingWidth == 0)
                {
                    break;
                }

                string text = segment.Text.Length > remainingWidth
                    ? segment.Text[..remainingWidth]
                    : segment.Text;
                Console.ForegroundColor = segment.Color;
                Console.Write(text);
                remainingWidth -= text.Length;
            }

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(new string(' ', remainingWidth));
            Console.ResetColor();
        });
    }

    private void RenderFooter()
    {
        if (!TryGetDimensions(out _, out int height))
        {
            return;
        }

        RenderLine(
            height - 1,
            Footer,
            ConsoleColor.White,
            ConsoleColor.DarkBlue,
            center: true);
    }

    private void RenderLine(
        int row,
        string text,
        ConsoleColor foreground,
        ConsoleColor background = ConsoleColor.Black,
        bool center = false)
    {
        if (!TryGetDimensions(out int width, out int height)
            || row < 0
            || row >= height)
        {
            return;
        }

        TryRender(() =>
        {
            int writableWidth = Math.Max(width - 1, 1);
            string displayText = text.Length > writableWidth
                ? text[..writableWidth]
                : text;
            if (center && displayText.Length < writableWidth)
            {
                displayText = displayText.PadLeft(
                    displayText.Length + ((writableWidth - displayText.Length) / 2));
            }

            Console.SetCursorPosition(0, row);
            Console.ForegroundColor = foreground;
            Console.BackgroundColor = background;
            Console.Write(displayText.PadRight(writableWidth));
            Console.ResetColor();
        });
    }

    private void TrimEvents()
    {
        if (TryGetDimensions(out _, out int height))
        {
            TrimEvents(Math.Max((height - 1) - EventsStartRow, 0));
        }
    }

    private void TrimEvents(int capacity)
    {
        while (_events.Count > capacity)
        {
            _events.Dequeue();
        }
    }

    private bool TryGetDimensions(out int width, out int height)
    {
        width = 0;
        height = 0;

        if (!IsInteractive)
        {
            return false;
        }

        try
        {
            width = Console.WindowWidth;
            height = Console.WindowHeight;
            return width > 0 && height > EventsStartRow + 1;
        }
        catch (IOException exception)
        {
            LogRenderFailure(exception);
            return false;
        }
    }

    private void TryRender(Action render)
    {
        if (!IsInteractive)
        {
            return;
        }

        try
        {
            render();
        }
        catch (Exception exception) when (
            exception is IOException
            or ArgumentOutOfRangeException)
        {
            LogRenderFailure(exception);
        }
    }

    private void LogRenderFailure(Exception exception)
    {
        if (_renderFailureLogged)
        {
            return;
        }

        _renderFailureLogged = true;
        logger.LogWarning(exception, "The interactive console UI could not be rendered.");
    }

    private sealed record ConsoleEvent(IReadOnlyList<ConsoleSegment> Segments);

    private sealed record ConsoleSegment(string Text, ConsoleColor Color);
}

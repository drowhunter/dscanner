using System.Text;
using DirectInputWatcher;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DScanner.Services;

public sealed class ConsoleUiService(
    IOptions<DirectInputWatcherOptions> options,
    IConsoleKeyDispatcher keyDispatcher,
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
    private readonly DirectInputWatcherOptions _options = options.Value;
    private string _status = "Starting USB device watchers...";
    private string _loader = "Enumeration: waiting";
    private int _progressDots;
    private int _lastWidth;
    private int _lastHeight;
    private bool _renderFailureLogged;
    private string? _promptText;
    private StringBuilder? _promptInput;

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

    public ConsoleColor GetDeviceColor(Guid deviceId)
    {
        ConsoleColor[] brightColors =
        [
            ConsoleColor.Cyan,
            ConsoleColor.Green,
            ConsoleColor.Yellow,
            ConsoleColor.Magenta,
            ConsoleColor.Red,
            ConsoleColor.Blue,
            ConsoleColor.White,
            ConsoleColor.DarkYellow
        ];

        int colorIndex = Math.Abs(deviceId.GetHashCode()) % brightColors.Length;
        return brightColors[colorIndex];
    }

    public Task<string?> ReadLabelAsync(string prompt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        if (!keyDispatcher.IsAvailable)
        {
            return Task.FromResult<string?>(null);
        }

        return ReadLabelCoreAsync(prompt, cancellationToken);
    }

    public void SetPrompt(string prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        lock (_gate)
        {
            bool wasHidden = _promptText is null;
            _promptText = prompt;
            _promptInput = null;

            if (wasHidden)
            {
                // The prompt row takes a line away from the event log.
                TrimEvents();
                RenderEvents();
            }

            RenderPrompt();
        }
    }

    public void ClearPrompt()
    {
        lock (_gate)
        {
            if (_promptText is null)
            {
                return;
            }

            _promptText = null;
            _promptInput = null;
            RenderEvents();
            RenderFooter();
        }
    }

    private async Task<string?> ReadLabelCoreAsync(
        string prompt,
        CancellationToken cancellationToken)
    {
        TaskCompletionSource<string?> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        StringBuilder buffer = new();

        lock (_gate)
        {
            bool wasHidden = _promptText is null;
            _promptText = prompt;
            _promptInput = buffer;

            if (wasHidden)
            {
                TrimEvents();
                RenderEvents();
            }

            RenderPrompt();
        }

        using IDisposable capture = keyDispatcher.Capture(key =>
        {
            lock (_gate)
            {
                switch (key.Key)
                {
                    case ConsoleKey.Enter:
                        completion.TrySetResult(buffer.ToString().Trim());
                        return true;

                    case ConsoleKey.Escape:
                        completion.TrySetResult(null);
                        return true;

                    case ConsoleKey.Backspace:
                        if (buffer.Length > 0)
                        {
                            buffer.Length--;
                            RenderPrompt();
                        }

                        return true;
                }

                if (!char.IsControl(key.KeyChar) && key.KeyChar != '\0')
                {
                    buffer.Append(key.KeyChar);
                    RenderPrompt();
                    return true;
                }

                return false;
            }
        });

        await using CancellationTokenRegistration registration =
            cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));

        try
        {
            return await completion.Task;
        }
        finally
        {
            lock (_gate)
            {
                _promptInput = null;
                RenderPrompt();
            }
        }
    }

    private void RenderPrompt()
    {
        if (_promptText is null
            || !TryGetDimensions(out _, out int height))
        {
            return;
        }

        string text = _promptInput is null
            ? _promptText
            : $"{_promptText}{_promptInput}_";

        RenderLine(height - 2, text, ConsoleColor.Cyan);
    }

    private int GetEventRowCount(int height)
    {
        int reservedRows = _promptText is null ? 1 : 2;
        return Math.Max(height - reservedRows - EventsStartRow, 0);
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
                    $"USB DirectInput devices | {_options.PollFrequency} Hz | Fast enumeration",
                    ConsoleColor.Gray);
                RenderLine(LoaderRow, _loader, ConsoleColor.Yellow);
                RenderLine(EventsHeaderRow, "Input and USB events", ConsoleColor.DarkCyan);
                RenderEvents();
                RenderPrompt();
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

        int eventRowCount = GetEventRowCount(height);
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
            TrimEvents(GetEventRowCount(height));
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

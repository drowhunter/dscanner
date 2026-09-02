using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DScanner.Services;

/// <summary>
/// The single reader of console keystrokes. Ctrl+Q always shuts the application down;
/// every other key is offered to the active <see cref="IConsoleKeyDispatcher"/> capture.
/// </summary>
public sealed class ConsoleKeyPump(
    IConsoleKeySource keySource,
    IHostApplicationLifetime applicationLifetime,
    ILogger<ConsoleKeyPump> logger)
    : BackgroundService, IConsoleKeyDispatcher
{
    private static readonly TimeSpan KeyPollInterval = TimeSpan.FromMilliseconds(50);

    private readonly object _gate = new();
    private readonly Stack<Func<ConsoleKeyInfo, bool>> _handlers = new();

    public bool IsAvailable => keySource.IsAvailable;

    public IDisposable Capture(Func<ConsoleKeyInfo, bool> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_gate)
        {
            _handlers.Push(handler);
        }

        return new CaptureToken(this, handler);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!keySource.IsAvailable)
        {
            logger.LogDebug("Console key handling is unavailable because console input is redirected.");
            return;
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                while (keySource.TryReadKey(out ConsoleKeyInfo key))
                {
                    if (IsQuitKey(key))
                    {
                        logger.LogInformation("Ctrl+Q pressed; shutting down.");
                        applicationLifetime.StopApplication();
                        return;
                    }

                    Dispatch(key);
                }

                await Task.Delay(KeyPollInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning(exception, "Console input became unavailable; key handling is disabled.");
        }
        catch (IOException exception)
        {
            logger.LogWarning(exception, "Console input could not be read; key handling is disabled.");
        }
    }

    public static bool IsQuitKey(ConsoleKeyInfo key) =>
        key.Key == ConsoleKey.Q
        && key.Modifiers.HasFlag(ConsoleModifiers.Control);

    private void Dispatch(ConsoleKeyInfo key)
    {
        Func<ConsoleKeyInfo, bool>? handler;
        lock (_gate)
        {
            _handlers.TryPeek(out handler);
        }

        try
        {
            handler?.Invoke(key);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "A console key handler failed.");
        }
    }

    private void Release(Func<ConsoleKeyInfo, bool> handler)
    {
        lock (_gate)
        {
            if (_handlers.Count == 0)
            {
                return;
            }

            // Captures are expected to unwind in order; tolerate out-of-order disposal.
            List<Func<ConsoleKeyInfo, bool>> retained = [.. _handlers];
            if (!retained.Remove(handler))
            {
                return;
            }

            _handlers.Clear();
            for (int index = retained.Count - 1; index >= 0; index--)
            {
                _handlers.Push(retained[index]);
            }
        }
    }

    private sealed class CaptureToken(
        ConsoleKeyPump pump,
        Func<ConsoleKeyInfo, bool> handler)
        : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            pump.Release(handler);
        }
    }
}

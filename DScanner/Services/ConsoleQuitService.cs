using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DScanner.Services;

public sealed class ConsoleQuitService(
    IConsoleKeySource keySource,
    IHostApplicationLifetime applicationLifetime,
    ILogger<ConsoleQuitService> logger)
    : BackgroundService
{
    private static readonly TimeSpan KeyPollInterval = TimeSpan.FromMilliseconds(50);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!keySource.IsAvailable)
        {
            logger.LogDebug("Ctrl+Q shutdown is unavailable because console input is redirected.");
            return;
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                while (keySource.TryReadKey(out ConsoleKeyInfo key))
                {
                    if (!IsQuitKey(key))
                    {
                        continue;
                    }

                    logger.LogInformation("Ctrl+Q pressed; shutting down.");
                    applicationLifetime.StopApplication();
                    return;
                }

                await Task.Delay(KeyPollInterval, stoppingToken);
            }
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning(exception, "Console input became unavailable; Ctrl+Q shutdown is disabled.");
        }
        catch (IOException exception)
        {
            logger.LogWarning(exception, "Console input could not be read; Ctrl+Q shutdown is disabled.");
        }
    }

    public static bool IsQuitKey(ConsoleKeyInfo key) =>
        key.Key == ConsoleKey.Q
        && key.Modifiers.HasFlag(ConsoleModifiers.Control);
}

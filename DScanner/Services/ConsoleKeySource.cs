namespace DScanner.Services;

public sealed class ConsoleKeySource : IConsoleKeySource
{
    public bool IsAvailable => !Console.IsInputRedirected;

    public bool TryReadKey(out ConsoleKeyInfo key)
    {
        if (!Console.KeyAvailable)
        {
            key = default;
            return false;
        }

        key = Console.ReadKey(intercept: true);
        return true;
    }
}

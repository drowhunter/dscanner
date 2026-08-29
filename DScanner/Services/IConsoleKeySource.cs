namespace DScanner.Services;

public interface IConsoleKeySource
{
    bool IsAvailable { get; }

    bool TryReadKey(out ConsoleKeyInfo key);
}

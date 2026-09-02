namespace DScanner.Services;

/// <summary>
/// Routes console keystrokes to at most one active capture handler.
/// </summary>
public interface IConsoleKeyDispatcher
{
    /// <summary>
    /// Gets a value indicating whether console keystrokes can be read at all.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Directs keystrokes to <paramref name="handler"/> until the returned token is disposed.
    /// The handler returns <see langword="true"/> when it consumed the key.
    /// A nested capture takes over and restores the previous handler when disposed.
    /// </summary>
    IDisposable Capture(Func<ConsoleKeyInfo, bool> handler);
}

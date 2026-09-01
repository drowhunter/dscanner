namespace DScanner.Services;

public interface IConsoleUi
{
    void SetStatus(string status);

    void BeginEnumeration();

    void AdvanceEnumeration();

    void EndEnumeration(TimeSpan elapsed);

    void AddEvent(string message, ConsoleColor color = ConsoleColor.White);

    void AddHighlightedEvent(
        string highlightedText,
        string remainingText,
        ConsoleColor highlightColor);

    ConsoleColor GetDeviceColor(Guid deviceId);

    /// <summary>
    /// Shows <paramref name="prompt"/> on the prompt row and reads an edited line of text.
    /// </summary>
    /// <returns>
    /// The entered text, or <see langword="null"/> when the user pressed Escape or console
    /// input is unavailable.
    /// </returns>
    Task<string?> ReadLabelAsync(string prompt, CancellationToken cancellationToken);

    /// <summary>
    /// Shows a message on the prompt row without reading input.
    /// </summary>
    void SetPrompt(string prompt);

    /// <summary>
    /// Hides the prompt row and returns the space to the event log.
    /// </summary>
    void ClearPrompt();
}

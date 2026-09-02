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
    /// When set, only events associated with the given device id will be shown
    /// for device-specific event methods. Pass <c>null</c> to clear the focus.
    /// </summary>
    void SetFocusedDevice(Guid? deviceId);

    /// <summary>
    /// Adds an event that is associated with a specific device. When a focused
    /// device is set, only events for that device will be rendered.
    /// </summary>
    void AddDeviceEvent(Guid deviceId, string message, ConsoleColor color = ConsoleColor.White);

    /// <summary>
    /// Adds a highlighted device event (device name highlighted separately).
    /// </summary>
    void AddDeviceHighlightedEvent(Guid deviceId, string highlightedText, string remainingText, ConsoleColor highlightColor);

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

    // (No backward-compat shim required) 
}

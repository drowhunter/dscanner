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
}

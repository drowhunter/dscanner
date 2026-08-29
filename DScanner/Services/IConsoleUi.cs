namespace DScanner.Services;

public interface IConsoleUi
{
    void SetStatus(string status);

    void BeginEnumeration();

    void AdvanceEnumeration();

    void EndEnumeration(TimeSpan elapsed);

    void AddEvent(string message);
}

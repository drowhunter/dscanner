using DScanner.Services;

namespace DScanner.Tests.Fakes;

/// <summary>
/// Drives the mapping loop deterministically: each call to <see cref="SetPrompt"/> runs the
/// next scripted action, which is the point at which the service is listening for input.
/// </summary>
internal sealed class FakeConsoleUi : IConsoleUi
{
    public Queue<string?> Labels { get; } = new();

    public Queue<Action> PromptActions { get; } = new();

    public List<string> Events { get; } = [];

    public List<string> Prompts { get; } = [];

    public bool PromptCleared { get; private set; }

    public Task<string?> ReadLabelAsync(string prompt, CancellationToken cancellationToken) =>
        Task.FromResult(Labels.Count > 0 ? Labels.Dequeue() : null);

    public void SetPrompt(string prompt)
    {
        Prompts.Add(prompt);

        if (PromptActions.Count > 0)
        {
            PromptActions.Dequeue().Invoke();
        }
    }

    public void ClearPrompt() => PromptCleared = true;

    public void AddEvent(string message, ConsoleColor color = ConsoleColor.White) =>
        Events.Add(message);

    public void AddHighlightedEvent(
        string highlightedText,
        string remainingText,
        ConsoleColor highlightColor) =>
        Events.Add($"{highlightedText}{remainingText}");

    public ConsoleColor GetDeviceColor(Guid deviceId) => ConsoleColor.White;

    public void SetFocusedDevice(Guid? deviceId) { }

    public void AddDeviceEvent(Guid deviceId, string message, ConsoleColor color = ConsoleColor.White) =>
        AddEvent(message, color);

    public void AddDeviceHighlightedEvent(Guid deviceId, string highlightedText, string remainingText, ConsoleColor highlightColor) =>
        AddHighlightedEvent(highlightedText, remainingText, highlightColor);

    public void SetStatus(string status)
    {
    }

    public void BeginEnumeration()
    {
    }

    public void AdvanceEnumeration()
    {
    }

    public void EndEnumeration(TimeSpan elapsed)
    {
    }
}

using DScanner.Services;

namespace DScanner.Tests.Fakes;

internal sealed class FakeConsoleKeyDispatcher : IConsoleKeyDispatcher
{
    private readonly List<Func<ConsoleKeyInfo, bool>> _handlers = [];

    public bool IsAvailable { get; set; } = true;

    public IDisposable Capture(Func<ConsoleKeyInfo, bool> handler)
    {
        _handlers.Add(handler);
        return new Token(_handlers, handler);
    }

    public void Press(ConsoleKey key)
    {
        if (_handlers.Count == 0)
        {
            return;
        }

        _handlers[^1].Invoke(new ConsoleKeyInfo('\0', key, false, false, false));
    }

    private sealed class Token(
        List<Func<ConsoleKeyInfo, bool>> handlers,
        Func<ConsoleKeyInfo, bool> handler)
        : IDisposable
    {
        public void Dispose() => handlers.Remove(handler);
    }
}

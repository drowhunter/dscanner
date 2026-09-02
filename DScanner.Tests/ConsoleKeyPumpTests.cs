using DScanner.Services;
using DScanner.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace DScanner.Tests;

public sealed class ConsoleKeyPumpTests
{
    [Fact]
    public async Task CtrlQ_StopsApplication()
    {
        FakeApplicationLifetime lifetime = new();
        FakeConsoleKeySource keys = new(CtrlQ);
        ConsoleKeyPump pump = Create(keys, lifetime);

        await pump.StartAsync(CancellationToken.None);
        await lifetime.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await pump.StopAsync(CancellationToken.None);

        Assert.True(lifetime.StopRequested);
    }

    [Fact]
    public async Task CtrlQ_StopsApplication_WhileACaptureIsActive()
    {
        FakeApplicationLifetime lifetime = new();
        FakeConsoleKeySource keys = new(
            new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false),
            CtrlQ);
        ConsoleKeyPump pump = Create(keys, lifetime);

        List<ConsoleKeyInfo> captured = [];
        using IDisposable capture = pump.Capture(key =>
        {
            captured.Add(key);
            return true;
        });

        await pump.StartAsync(CancellationToken.None);
        await lifetime.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await pump.StopAsync(CancellationToken.None);

        Assert.True(lifetime.StopRequested);
        Assert.Equal(ConsoleKey.A, Assert.Single(captured).Key);
    }

    [Fact]
    public async Task DisposingACapture_RestoresThePreviousHandler()
    {
        FakeApplicationLifetime lifetime = new();
        FakeConsoleKeySource keys = new(
            new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false));
        ConsoleKeyPump pump = Create(keys, lifetime);

        List<ConsoleKeyInfo> outer = [];
        List<ConsoleKeyInfo> inner = [];

        using IDisposable outerCapture = pump.Capture(key =>
        {
            outer.Add(key);
            return true;
        });

        IDisposable innerCapture = pump.Capture(key =>
        {
            inner.Add(key);
            return true;
        });
        innerCapture.Dispose();

        await pump.StartAsync(CancellationToken.None);
        await keys.Drained.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await pump.StopAsync(CancellationToken.None);

        Assert.Empty(inner);
        Assert.Equal(ConsoleKey.A, Assert.Single(outer).Key);
    }

    [Theory]
    [InlineData(ConsoleKey.Q, (int)ConsoleModifiers.Control, true)]
    [InlineData(ConsoleKey.Q, (int)(ConsoleModifiers.Control | ConsoleModifiers.Shift), true)]
    [InlineData(ConsoleKey.Q, 0, false)]
    [InlineData(ConsoleKey.C, (int)ConsoleModifiers.Control, false)]
    public void IsQuitKey_RecognizesCtrlQ(
        ConsoleKey key,
        int modifierValue,
        bool expected)
    {
        ConsoleModifiers modifiers = (ConsoleModifiers)modifierValue;
        ConsoleKeyInfo keyInfo = new(
            '\0',
            key,
            modifiers.HasFlag(ConsoleModifiers.Shift),
            modifiers.HasFlag(ConsoleModifiers.Alt),
            modifiers.HasFlag(ConsoleModifiers.Control));

        Assert.Equal(expected, ConsoleKeyPump.IsQuitKey(keyInfo));
    }

    private static ConsoleKeyInfo CtrlQ =>
        new('q', ConsoleKey.Q, shift: false, alt: false, control: true);

    private static ConsoleKeyPump Create(
        IConsoleKeySource keys,
        FakeApplicationLifetime lifetime) =>
        new(keys, lifetime, NullLogger<ConsoleKeyPump>.Instance);

    private sealed class FakeConsoleKeySource(params ConsoleKeyInfo[] keys) : IConsoleKeySource
    {
        private readonly Queue<ConsoleKeyInfo> _keys = new(keys);

        public TaskCompletionSource Drained { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsAvailable => true;

        public bool TryReadKey(out ConsoleKeyInfo result)
        {
            if (_keys.TryDequeue(out result))
            {
                return true;
            }

            Drained.TrySetResult();
            return false;
        }
    }
}

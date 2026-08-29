using DScanner.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace DScanner.Tests;

public sealed class ConsoleQuitServiceTests
{
    [Fact]
    public async Task CtrlQ_StopsApplication()
    {
        FakeApplicationLifetime lifetime = new();
        FakeConsoleKeySource keys = new(
            new ConsoleKeyInfo(
                'q',
                ConsoleKey.Q,
                shift: false,
                alt: false,
                control: true));
        ConsoleQuitService service = new(
            keys,
            lifetime,
            NullLogger<ConsoleQuitService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await lifetime.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await service.StopAsync(CancellationToken.None);

        Assert.True(lifetime.StopRequested);
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

        Assert.Equal(expected, ConsoleQuitService.IsQuitKey(keyInfo));
    }

    private sealed class FakeConsoleKeySource(ConsoleKeyInfo key) : IConsoleKeySource
    {
        private bool _hasKey = true;

        public bool IsAvailable => true;

        public bool TryReadKey(out ConsoleKeyInfo result)
        {
            if (_hasKey)
            {
                _hasKey = false;
                result = key;
                return true;
            }

            result = default;
            return false;
        }
    }

    private sealed class FakeApplicationLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _started = new();
        private readonly CancellationTokenSource _stopping = new();
        private readonly CancellationTokenSource _stopped = new();

        public bool StopRequested { get; private set; }

        public TaskCompletionSource Stopped { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken ApplicationStarted => _started.Token;

        public CancellationToken ApplicationStopping => _stopping.Token;

        public CancellationToken ApplicationStopped => _stopped.Token;

        public void StopApplication()
        {
            StopRequested = true;
            _stopping.Cancel();
            _stopped.Cancel();
            Stopped.TrySetResult();
        }
    }
}

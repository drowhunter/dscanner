using Microsoft.Extensions.Hosting;

namespace DScanner.Tests.Fakes;

internal sealed class FakeApplicationLifetime : IHostApplicationLifetime
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

using ServerPilot.Agent.Looping;

namespace ServerPilot.UnitTests.AgentLooping;

public sealed class PeriodicAgentLoopTests
{
    [Fact]
    public async Task DoesNotStartAnotherIterationBeforeThePreviousOneCompletes()
    {
        BlockingAgentDelay delay = new();
        PeriodicAgentLoop loop = new(delay);
        TaskCompletionSource<bool> iterationStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> completeIteration = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenSource cancellation = new();
        int iterations = 0;

        Task running = loop.RunAsync(
            TimeSpan.FromSeconds(1),
            async token =>
            {
                iterations++;
                iterationStarted.TrySetResult(true);
                await completeIteration.Task.WaitAsync(token);
                return true;
            },
            cancellation.Token);
        await iterationStarted.Task;

        Assert.Equal(1, iterations);

        completeIteration.TrySetResult(true);
        await delay.Entered.Task;
        Assert.Equal(1, iterations);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
    }

    [Fact]
    public async Task DoesNotScheduleAnotherIterationWhenTheCurrentOneStopsTheLoop()
    {
        RecordingAgentDelay delay = new();
        PeriodicAgentLoop loop = new(delay);
        int iterations = 0;

        await loop.RunAsync(
            TimeSpan.FromSeconds(1),
            _ =>
            {
                iterations++;
                return Task.FromResult(false);
            },
            CancellationToken.None);

        Assert.Equal(1, iterations);
        Assert.Empty(delay.Delays);
    }

    private sealed class RecordingAgentDelay : IAgentDelay
    {
        public List<TimeSpan> Delays { get; } = [];

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Delays.Add(delay);
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingAgentDelay : IAgentDelay
    {
        public TaskCompletionSource<bool> Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Entered.TrySetResult(true);
            return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
}

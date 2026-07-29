using System.Net;
using System.Net.Http;
using ServerPilot.Agent.Api;
using ServerPilot.Agent.Looping;

namespace ServerPilot.UnitTests.AgentLooping;

public sealed class AgentRetryExecutorTests
{
    [Fact]
    public async Task RetriesTransientNetworkFailuresWithBoundedDelays()
    {
        RecordingAgentDelay delay = new();
        AgentRetryExecutor retry = new(delay);
        int attempts = 0;

        string result = await retry.ExecuteAsync(
            _ =>
            {
                attempts++;
                return attempts < 3
                    ? Task.FromException<string>(new HttpRequestException("temporary"))
                    : Task.FromResult("completed");
            },
            CancellationToken.None);

        Assert.Equal("completed", result);
        Assert.Equal(3, attempts);
        Assert.Equal(2, delay.Delays.Count);
        Assert.All(delay.Delays, value => Assert.InRange(value, TimeSpan.FromMilliseconds(750), TimeSpan.FromSeconds(2.5)));
    }

    [Fact]
    public async Task DoesNotRetryAuthenticationFailure()
    {
        RecordingAgentDelay delay = new();
        AgentRetryExecutor retry = new(delay);
        int attempts = 0;

        await Assert.ThrowsAsync<AgentApiException>(
            () => retry.ExecuteAsync(
                _ =>
                {
                    attempts++;
                    return Task.FromException(new AgentApiException(HttpStatusCode.Unauthorized));
                },
                CancellationToken.None));

        Assert.Equal(1, attempts);
        Assert.Empty(delay.Delays);
    }

    [Fact]
    public async Task CancellationStopsARetryDelayPromptly()
    {
        BlockingAgentDelay delay = new();
        AgentRetryExecutor retry = new(delay);
        using CancellationTokenSource cancellation = new();

        Task operation = retry.ExecuteAsync(
            _ => Task.FromException(new HttpRequestException("temporary")),
            cancellation.Token);
        await delay.Entered.Task;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
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

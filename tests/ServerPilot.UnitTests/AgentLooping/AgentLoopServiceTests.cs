using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using ServerPilot.Agent.Api;
using ServerPilot.Agent.Configuration;
using ServerPilot.Agent.Credentials;
using ServerPilot.Agent.Looping;

namespace ServerPilot.UnitTests.AgentLooping;

public sealed class AgentLoopServiceTests
{
    [Fact]
    public async Task HoldsOneClaimedCommandInsteadOfPollingForAnother()
    {
        BlockingAgentDelay delay = new(requiredEntries: 2);
        RecordingAgentApiClient apiClient = new(CreateCommand());
        AgentLoopService service = CreateService(apiClient, delay);
        using CancellationTokenSource cancellation = new();

        Task running = service.RunAsync(CreateCredential(), cancellation.Token);
        await apiClient.CommandClaimed.Task;
        await delay.RequiredEntriesReached.Task;

        Assert.Equal(1, apiClient.ClaimCalls);

        cancellation.Cancel();
        await running;
    }

    [Fact]
    public async Task StopsBothLoopsAfterAnAuthenticationFailure()
    {
        BlockingAgentDelay delay = new(requiredEntries: 1);
        AuthenticationFailingAgentApiClient apiClient = new();
        AgentLoopService service = CreateService(apiClient, delay);

        AgentLoopFatalException exception = await Assert.ThrowsAsync<AgentLoopFatalException>(
            () => service.RunAsync(CreateCredential(), CancellationToken.None));

        Assert.Equal(AgentApiFailureKind.Authentication, exception.FailureKind);
        Assert.Equal(1, apiClient.HeartbeatCalls);
    }

    private static AgentLoopService CreateService(IAgentApiClient apiClient, IAgentDelay delay)
    {
        AgentOptions options = new()
        {
            ApiBaseUrl = "https://api.example.test",
            Name = "test-agent",
            HeartbeatIntervalSeconds = 60,
            CommandPollingIntervalSeconds = 60,
        };
        options.Validate();

        return new AgentLoopService(
            options,
            apiClient,
            new AgentRetryExecutor(delay),
            new PeriodicAgentLoop(delay),
            NullLogger<AgentLoopService>.Instance);
    }

    private static AgentCredential CreateCredential() => AgentCredential.Create(
        Guid.NewGuid(),
        "spac_0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF",
        AgentCredential.ExpectedAuthorizationScheme);

    private static ClaimedAgentCommand CreateCommand() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "StartServer",
        Guid.NewGuid(),
        "New");

    private sealed class RecordingAgentApiClient(ClaimedAgentCommand command) : IAgentApiClient
    {
        public TaskCompletionSource<bool> CommandClaimed { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int ClaimCalls { get; private set; }

        public Task SendHeartbeatAsync(AgentCredential credential, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<ClaimedAgentCommand?> ClaimNextCommandAsync(
            AgentCredential credential,
            CancellationToken cancellationToken)
        {
            ClaimCalls++;
            CommandClaimed.TrySetResult(true);
            return Task.FromResult<ClaimedAgentCommand?>(command);
        }
    }

    private sealed class AuthenticationFailingAgentApiClient : IAgentApiClient
    {
        public int HeartbeatCalls { get; private set; }

        public Task SendHeartbeatAsync(AgentCredential credential, CancellationToken cancellationToken)
        {
            HeartbeatCalls++;
            return Task.FromException(new AgentApiException(HttpStatusCode.Unauthorized));
        }

        public Task<ClaimedAgentCommand?> ClaimNextCommandAsync(
            AgentCredential credential,
            CancellationToken cancellationToken) =>
            Task.FromResult<ClaimedAgentCommand?>(null);
    }

    private sealed class BlockingAgentDelay(int requiredEntries) : IAgentDelay
    {
        private int entries;

        public TaskCompletionSource<bool> RequiredEntriesReached { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref entries) >= requiredEntries)
            {
                RequiredEntriesReached.TrySetResult(true);
            }

            return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
}

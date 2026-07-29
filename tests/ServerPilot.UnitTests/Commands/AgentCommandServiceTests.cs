using ServerPilot.Application.Commands;
using ServerPilot.Domain.Commands;

namespace ServerPilot.UnitTests.Commands;

public sealed class AgentCommandServiceTests
{
    private static readonly DateTimeOffset UtcNow =
        new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ClaimPassesAuthenticatedAgentAndCurrentUtcTime()
    {
        RecordingServerCommandRepository repository = new();
        AgentCommandService service = new(repository, new FixedTimeProvider(UtcNow));
        Guid agentId = Guid.NewGuid();

        await service.ClaimNextAsync(agentId, CancellationToken.None);

        Assert.Equal(agentId, repository.AgentId);
        Assert.Equal(UtcNow, repository.TransitionAt);
        Assert.Equal(1, repository.ClaimCalls);
    }

    [Fact]
    public async Task FailTrimsBoundedDetailsBeforeCallingRepository()
    {
        RecordingServerCommandRepository repository = new();
        AgentCommandService service = new(repository, new FixedTimeProvider(UtcNow));

        AgentCommandTransitionStatus status = await service.FailAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            " ProcessFailed ",
            " Process could not be started. ",
            CancellationToken.None);

        Assert.Equal(AgentCommandTransitionStatus.Succeeded, status);
        Assert.Equal("ProcessFailed", repository.ErrorCode);
        Assert.Equal("Process could not be started.", repository.ErrorMessage);
        Assert.Equal(1, repository.FailCalls);
    }

    [Theory]
    [InlineData(null, "message")]
    [InlineData("", "message")]
    [InlineData("   ", "message")]
    [InlineData("code", null)]
    [InlineData("code", "  ")]
    public async Task FailRejectsMissingDetailsWithoutCallingRepository(
        string? errorCode,
        string? errorMessage)
    {
        RecordingServerCommandRepository repository = new();
        AgentCommandService service = new(repository, new FixedTimeProvider(UtcNow));

        AgentCommandTransitionStatus status = await service.FailAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            errorCode,
            errorMessage,
            CancellationToken.None);

        Assert.Equal(AgentCommandTransitionStatus.InvalidFailureDetails, status);
        Assert.Equal(0, repository.FailCalls);
    }

    [Fact]
    public async Task FailRejectsOversizedDetailsWithoutCallingRepository()
    {
        RecordingServerCommandRepository repository = new();
        AgentCommandService service = new(repository, new FixedTimeProvider(UtcNow));

        AgentCommandTransitionStatus status = await service.FailAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new string('x', ServerCommand.MaximumErrorCodeLength + 1),
            "message",
            CancellationToken.None);

        Assert.Equal(AgentCommandTransitionStatus.InvalidFailureDetails, status);
        Assert.Equal(0, repository.FailCalls);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class RecordingServerCommandRepository : IServerCommandRepository
    {
        public int ClaimCalls { get; private set; }
        public int FailCalls { get; private set; }
        public Guid? AgentId { get; private set; }
        public DateTimeOffset? TransitionAt { get; private set; }
        public string? ErrorCode { get; private set; }
        public string? ErrorMessage { get; private set; }

        public Task<ClaimedServerCommandDetails?> ClaimNextAsync(
            Guid agentId,
            DateTimeOffset claimedAt,
            CancellationToken cancellationToken)
        {
            ClaimCalls++;
            AgentId = agentId;
            TransitionAt = claimedAt;
            return Task.FromResult<ClaimedServerCommandDetails?>(null);
        }

        public Task<AgentCommandTransitionStatus> StartAsync(
            Guid commandId,
            Guid agentId,
            DateTimeOffset startedAt,
            CancellationToken cancellationToken) =>
            Task.FromResult(AgentCommandTransitionStatus.Succeeded);

        public Task<AgentCommandTransitionStatus> CompleteAsync(
            Guid commandId,
            Guid agentId,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken) =>
            Task.FromResult(AgentCommandTransitionStatus.Succeeded);

        public Task<AgentCommandTransitionStatus> FailAsync(
            Guid commandId,
            Guid agentId,
            DateTimeOffset completedAt,
            string errorCode,
            string errorMessage,
            CancellationToken cancellationToken)
        {
            FailCalls++;
            AgentId = agentId;
            TransitionAt = completedAt;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
            return Task.FromResult(AgentCommandTransitionStatus.Succeeded);
        }

        public Task<CreateServerCommandResult> CreateOwnedAsync(
            Guid serverInstanceId,
            Guid userId,
            ServerCommandType type,
            DateTimeOffset createdAt,
            Guid correlationId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ServerCommandHistoryResult> ListOwnedAsync(
            Guid serverInstanceId,
            Guid userId,
            ServerCommandHistoryCursor? after,
            int limit,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}

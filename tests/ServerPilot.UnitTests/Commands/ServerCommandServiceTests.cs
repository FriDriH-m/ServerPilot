using ServerPilot.Application.Commands;
using ServerPilot.Domain.Commands;

namespace ServerPilot.UnitTests.Commands;

public sealed class ServerCommandServiceTests
{
    [Fact]
    public async Task CreateRejectsUnsupportedCommandTypeWithoutCallingRepository()
    {
        RecordingServerCommandRepository repository = new();
        ServerCommandService service = new(repository, TimeProvider.System);

        CreateServerCommandResult result = await service.CreateAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            (ServerCommandType)999,
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(CreateServerCommandStatus.UnsupportedType, result.Status);
        Assert.Null(result.Command);
        Assert.Equal(0, repository.CreateCalls);
    }

    [Fact]
    public async Task CreatePassesRequestCorrelationIdToRepository()
    {
        RecordingServerCommandRepository repository = new();
        ServerCommandService service = new(repository, TimeProvider.System);
        Guid correlationId = Guid.NewGuid();

        await service.CreateAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ServerCommandType.StartServer,
            correlationId,
            CancellationToken.None);

        Assert.Equal(correlationId, repository.CorrelationId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task ListRejectsInvalidLimitBeforeCallingRepository(int limit)
    {
        RecordingServerCommandRepository repository = new();
        ServerCommandService service = new(repository, TimeProvider.System);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.ListAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            limit,
            CancellationToken.None));

        Assert.Equal(0, repository.ListCalls);
    }

    private sealed class RecordingServerCommandRepository : IServerCommandRepository
    {
        public int CreateCalls { get; private set; }

        public int ListCalls { get; private set; }

        public Guid? CorrelationId { get; private set; }

        public Task<ClaimedServerCommandDetails?> ClaimNextAsync(
            Guid agentId,
            DateTimeOffset claimedAt,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AgentCommandTransitionStatus> StartAsync(
            Guid commandId,
            Guid agentId,
            DateTimeOffset startedAt,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AgentCommandTransitionStatus> CompleteAsync(
            Guid commandId,
            Guid agentId,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AgentCommandTransitionStatus> FailAsync(
            Guid commandId,
            Guid agentId,
            DateTimeOffset completedAt,
            string errorCode,
            string errorMessage,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CreateServerCommandResult> CreateOwnedAsync(
            Guid serverInstanceId,
            Guid userId,
            ServerCommandType type,
            DateTimeOffset createdAt,
            Guid correlationId,
            CancellationToken cancellationToken)
        {
            CreateCalls++;
            CorrelationId = correlationId;
            return Task.FromResult(new CreateServerCommandResult(
                CreateServerCommandStatus.ServerInstanceNotFound,
                null));
        }

        public Task<ServerCommandHistoryResult> ListOwnedAsync(
            Guid serverInstanceId,
            Guid userId,
            ServerCommandHistoryCursor? after,
            int limit,
            CancellationToken cancellationToken)
        {
            ListCalls++;
            return Task.FromResult(new ServerCommandHistoryResult(false, [], false));
        }
    }
}

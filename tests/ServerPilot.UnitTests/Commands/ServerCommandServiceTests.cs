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
            CancellationToken.None);

        Assert.Equal(CreateServerCommandStatus.UnsupportedType, result.Status);
        Assert.Null(result.Command);
        Assert.Equal(0, repository.CreateCalls);
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(1, 0)]
    [InlineData(1_001, 50)]
    [InlineData(1, 101)]
    public async Task ListRejectsInvalidPaginationBeforeCallingRepository(int page, int limit)
    {
        RecordingServerCommandRepository repository = new();
        ServerCommandService service = new(repository, TimeProvider.System);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.ListAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            page,
            limit,
            CancellationToken.None));

        Assert.Equal(0, repository.ListCalls);
    }

    private sealed class RecordingServerCommandRepository : IServerCommandRepository
    {
        public int CreateCalls { get; private set; }

        public int ListCalls { get; private set; }

        public Task<ServerCommandDetails?> ClaimNextAsync(
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
            return Task.FromResult(new CreateServerCommandResult(
                CreateServerCommandStatus.ServerInstanceNotFound,
                null));
        }

        public Task<ServerCommandHistoryResult> ListOwnedAsync(
            Guid serverInstanceId,
            Guid userId,
            int skip,
            int limit,
            CancellationToken cancellationToken)
        {
            ListCalls++;
            return Task.FromResult(new ServerCommandHistoryResult(false, []));
        }
    }
}

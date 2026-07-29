using ServerPilot.Domain.Commands;

namespace ServerPilot.Application.Commands;

public interface IServerCommandRepository
{
    Task<ServerCommandDetails?> ClaimNextAsync(
        Guid agentId,
        DateTimeOffset claimedAt,
        CancellationToken cancellationToken);

    Task<AgentCommandTransitionStatus> StartAsync(
        Guid commandId,
        Guid agentId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken);

    Task<AgentCommandTransitionStatus> CompleteAsync(
        Guid commandId,
        Guid agentId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken);

    Task<AgentCommandTransitionStatus> FailAsync(
        Guid commandId,
        Guid agentId,
        DateTimeOffset completedAt,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken);

    Task<CreateServerCommandResult> CreateOwnedAsync(
        Guid serverInstanceId,
        Guid userId,
        ServerCommandType type,
        DateTimeOffset createdAt,
        Guid correlationId,
        CancellationToken cancellationToken);

    Task<ServerCommandHistoryResult> ListOwnedAsync(
        Guid serverInstanceId,
        Guid userId,
        int skip,
        int limit,
        CancellationToken cancellationToken);
}

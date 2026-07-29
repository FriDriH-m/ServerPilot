using ServerPilot.Domain.Commands;

namespace ServerPilot.Application.Commands;

public interface IServerCommandRepository
{
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

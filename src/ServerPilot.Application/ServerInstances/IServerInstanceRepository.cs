using ServerPilot.Domain.ServerInstances;

namespace ServerPilot.Application.ServerInstances;

public interface IServerInstanceRepository
{
    Task<bool> IsAgentOwnedByUserAsync(
        Guid agentId,
        Guid userId,
        CancellationToken cancellationToken);

    Task AddAsync(ServerInstance serverInstance, CancellationToken cancellationToken);

    Task<IReadOnlyList<ServerInstanceListItem>> ListOwnedAsync(
        Guid userId,
        int skip,
        int limit,
        CancellationToken cancellationToken);

    Task<ServerInstanceDetails?> FindOwnedAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken);

    Task<ServerInstanceDetails?> UpdateOwnedAsync(
        Guid id,
        Guid userId,
        ServerInstanceConfiguration configuration,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken);

    Task<DeleteServerInstanceStatus> DeleteOwnedAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken);
}

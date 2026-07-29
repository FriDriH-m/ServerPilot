using ServerPilot.Domain.ServerInstances;

namespace ServerPilot.Application.ServerInstances;

public interface IServerInstanceRepository
{
    Task<ServerInstanceAgentDetails?> FindAgentOwnedByUserAsync(
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

    Task<UpdateServerInstanceResult> UpdateOwnedAsync(
        Guid id,
        Guid userId,
        ServerInstanceConfiguration configuration,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken);

    Task<DeleteServerInstanceStatus> DeleteOwnedAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AssignedServerInstanceDetails>> ListAssignedAsync(
        Guid agentId,
        int skip,
        int limit,
        CancellationToken cancellationToken);

    Task<ServerInstanceStateReportResult> RecordProcessStateAsync(
        Guid agentId,
        Guid serverInstanceId,
        ServerInstanceStatus status,
        int? processId,
        DateTimeOffset? processStartedAt,
        DateTimeOffset reportedAt,
        CancellationToken cancellationToken);
}

using ServerPilot.Application.Agents;
using ServerPilot.Domain.ServerInstances;

namespace ServerPilot.Application.ServerInstances;

public sealed class ServerInstanceService(
    IServerInstanceRepository serverInstances,
    AgentAvailabilityOptions availabilityOptions,
    TimeProvider timeProvider)
{
    public async Task<ServerInstanceCreateResult> CreateAsync(
        Guid userId,
        Guid agentId,
        ServerInstanceConfiguration configuration,
        CancellationToken cancellationToken)
    {
        ValidateUserId(userId);
        ArgumentNullException.ThrowIfNull(configuration);
        ServerInstanceAgentDetails? agent = agentId == Guid.Empty
            ? null
            : await serverInstances.FindAgentOwnedByUserAsync(
                agentId,
                userId,
                cancellationToken);
        if (agent is null)
        {
            return new ServerInstanceCreateResult(
                ServerInstanceCreateStatus.AgentNotFound,
                null);
        }

        ServerInstance serverInstance = ServerInstance.Create(
            Guid.NewGuid(),
            agentId,
            configuration,
            timeProvider.GetUtcNow());
        await serverInstances.AddAsync(serverInstance, cancellationToken);

        return new ServerInstanceCreateResult(
            ServerInstanceCreateStatus.Succeeded,
            ApplyAvailability(MapDetails(serverInstance, agent.LastSeenAt), timeProvider.GetUtcNow()));
    }

    public async Task<IReadOnlyList<ServerInstanceListItem>> ListAsync(
        Guid userId,
        int page,
        int limit,
        CancellationToken cancellationToken)
    {
        ValidateUserId(userId);
        ValidatePagination(page, limit);
        IReadOnlyList<ServerInstanceListItem> items = await serverInstances.ListOwnedAsync(
            userId,
            (page - 1) * limit,
            limit,
            cancellationToken);
        DateTimeOffset now = timeProvider.GetUtcNow();
        return items.Select(item => ApplyAvailability(item, now)).ToArray();
    }

    public async Task<ServerInstanceDetails?> GetAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            return null;
        }

        ValidateUserId(userId);
        ServerInstanceDetails? details = await serverInstances.FindOwnedAsync(
            id,
            userId,
            cancellationToken);
        return details is null
            ? null
            : ApplyAvailability(details, timeProvider.GetUtcNow());
    }

    public async Task<UpdateServerInstanceResult> UpdateAsync(
        Guid id,
        Guid userId,
        ServerInstanceConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            return new UpdateServerInstanceResult(
                UpdateServerInstanceStatus.NotFound,
                null);
        }

        ValidateUserId(userId);
        ArgumentNullException.ThrowIfNull(configuration);
        UpdateServerInstanceResult result = await serverInstances.UpdateOwnedAsync(
            id,
            userId,
            configuration,
            timeProvider.GetUtcNow(),
            cancellationToken);
        return result.ServerInstance is null
            ? result
            : result with
            {
                ServerInstance = ApplyAvailability(
                    result.ServerInstance,
                    timeProvider.GetUtcNow()),
            };
    }

    public Task<DeleteServerInstanceStatus> DeleteAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            return Task.FromResult(DeleteServerInstanceStatus.NotFound);
        }

        ValidateUserId(userId);
        return serverInstances.DeleteOwnedAsync(id, userId, cancellationToken);
    }

    private static ServerInstanceDetails MapDetails(
        ServerInstance serverInstance,
        DateTimeOffset? agentLastSeenAt) =>
        new(
            serverInstance.Id,
            serverInstance.AgentId,
            serverInstance.Name,
            serverInstance.ExecutablePath,
            serverInstance.Arguments,
            serverInstance.WorkingDirectory,
            serverInstance.ProcessName,
            serverInstance.Status,
            serverInstance.Status,
            serverInstance.LastProcessId,
            serverInstance.LastProcessStartedAt,
            serverInstance.LastStatusReportedAt,
            false,
            agentLastSeenAt,
            serverInstance.CreatedAt,
            serverInstance.UpdatedAt);

    private ServerInstanceDetails ApplyAvailability(
        ServerInstanceDetails serverInstance,
        DateTimeOffset now)
    {
        bool stale = AgentAvailabilityEvaluator.Evaluate(
            serverInstance.AgentLastSeenAt,
            now,
            availabilityOptions.OfflineThreshold) == AgentAvailabilityStatus.Offline;
        return serverInstance with
        {
            Status = stale ? ServerInstanceStatus.Unreachable : serverInstance.ReportedStatus,
            IsStateStale = stale,
        };
    }

    private ServerInstanceListItem ApplyAvailability(
        ServerInstanceListItem serverInstance,
        DateTimeOffset now)
    {
        bool stale = AgentAvailabilityEvaluator.Evaluate(
            serverInstance.AgentLastSeenAt,
            now,
            availabilityOptions.OfflineThreshold) == AgentAvailabilityStatus.Offline;
        return serverInstance with
        {
            Status = stale ? ServerInstanceStatus.Unreachable : serverInstance.ReportedStatus,
            IsStateStale = stale,
        };
    }

    private static void ValidateUserId(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User ID cannot be empty.", nameof(userId));
        }
    }

    private static void ValidatePagination(int page, int limit)
    {
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                "Server instance list limit must be between 1 and 100.");
        }

        if (page is < 1 or > 1_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(page),
                "Server instance list page must be between 1 and 1000.");
        }
    }
}

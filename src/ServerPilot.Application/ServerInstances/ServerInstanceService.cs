using ServerPilot.Domain.ServerInstances;

namespace ServerPilot.Application.ServerInstances;

public sealed class ServerInstanceService(
    IServerInstanceRepository serverInstances,
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
        if (agentId == Guid.Empty || !await serverInstances.IsAgentOwnedByUserAsync(
                agentId,
                userId,
                cancellationToken))
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
            MapDetails(serverInstance));
    }

    public Task<IReadOnlyList<ServerInstanceListItem>> ListAsync(
        Guid userId,
        int page,
        int limit,
        CancellationToken cancellationToken)
    {
        ValidateUserId(userId);
        ValidatePagination(page, limit);
        return serverInstances.ListOwnedAsync(
            userId,
            (page - 1) * limit,
            limit,
            cancellationToken);
    }

    public Task<ServerInstanceDetails?> GetAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            return Task.FromResult<ServerInstanceDetails?>(null);
        }

        ValidateUserId(userId);
        return serverInstances.FindOwnedAsync(id, userId, cancellationToken);
    }

    public Task<ServerInstanceDetails?> UpdateAsync(
        Guid id,
        Guid userId,
        ServerInstanceConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            return Task.FromResult<ServerInstanceDetails?>(null);
        }

        ValidateUserId(userId);
        ArgumentNullException.ThrowIfNull(configuration);
        return serverInstances.UpdateOwnedAsync(
            id,
            userId,
            configuration,
            timeProvider.GetUtcNow(),
            cancellationToken);
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

    private static ServerInstanceDetails MapDetails(ServerInstance serverInstance) =>
        new(
            serverInstance.Id,
            serverInstance.AgentId,
            serverInstance.Name,
            serverInstance.ExecutablePath,
            serverInstance.Arguments,
            serverInstance.WorkingDirectory,
            serverInstance.ProcessName,
            serverInstance.Status,
            serverInstance.LastProcessId,
            serverInstance.CreatedAt,
            serverInstance.UpdatedAt);

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

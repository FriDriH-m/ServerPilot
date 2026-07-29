using Microsoft.EntityFrameworkCore;
using ServerPilot.Application.ServerInstances;
using ServerPilot.Domain.ServerInstances;
using ServerInstanceEntity = ServerPilot.Domain.ServerInstances.ServerInstance;

namespace ServerPilot.Infrastructure.Persistence;

internal sealed class ServerInstanceRepository(ServerPilotDbContext dbContext)
    : IServerInstanceRepository
{
    private static readonly ServerInstanceStatus[] ActiveStatuses =
    [
        ServerInstanceStatus.Starting,
        ServerInstanceStatus.Running,
        ServerInstanceStatus.Stopping,
    ];

    public Task<bool> IsAgentOwnedByUserAsync(
        Guid agentId,
        Guid userId,
        CancellationToken cancellationToken) =>
        dbContext.Agents
            .AsNoTracking()
            .AnyAsync(
                agent => agent.Id == agentId && agent.UserId == userId,
                cancellationToken);

    public async Task AddAsync(
        ServerInstanceEntity serverInstance,
        CancellationToken cancellationToken)
    {
        dbContext.ServerInstances.Add(serverInstance);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ServerInstanceListItem>> ListOwnedAsync(
        Guid userId,
        int skip,
        int limit,
        CancellationToken cancellationToken) =>
        await OwnedByUser(userId)
            .AsNoTracking()
            .OrderByDescending(serverInstance => serverInstance.CreatedAt)
            .ThenByDescending(serverInstance => serverInstance.Id)
            .Skip(skip)
            .Take(limit)
            .Select(serverInstance => new ServerInstanceListItem(
                serverInstance.Id,
                serverInstance.AgentId,
                serverInstance.Name,
                serverInstance.Status,
                serverInstance.LastProcessId,
                serverInstance.CreatedAt,
                serverInstance.UpdatedAt))
            .ToArrayAsync(cancellationToken);

    public Task<ServerInstanceDetails?> FindOwnedAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken) =>
        ProjectDetails(OwnedByUser(userId).Where(serverInstance => serverInstance.Id == id))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<ServerInstanceDetails?> UpdateOwnedAsync(
        Guid id,
        Guid userId,
        ServerInstanceConfiguration configuration,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        ServerInstanceEntity? serverInstance = await OwnedByUser(userId)
            .SingleOrDefaultAsync(serverInstance => serverInstance.Id == id, cancellationToken);
        if (serverInstance is null)
        {
            return null;
        }

        serverInstance.UpdateConfiguration(configuration, updatedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return MapDetails(serverInstance);
    }

    public async Task<DeleteServerInstanceStatus> DeleteOwnedAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            int deleted = await OwnedByUser(userId)
                .Where(serverInstance =>
                    serverInstance.Id == id &&
                    !ActiveStatuses.Contains(serverInstance.Status) &&
                    !dbContext.ServerCommands.Any(command =>
                        command.ServerInstanceId == serverInstance.Id))
                .ExecuteDeleteAsync(cancellationToken);
            if (deleted == 1)
            {
                return DeleteServerInstanceStatus.Succeeded;
            }

            ServerInstanceDeletionState? currentState = await OwnedByUser(userId)
                .AsNoTracking()
                .Where(serverInstance => serverInstance.Id == id)
                .Select(serverInstance => new ServerInstanceDeletionState(
                    serverInstance.Status,
                    dbContext.ServerCommands.Any(command =>
                        command.ServerInstanceId == serverInstance.Id)))
                .SingleOrDefaultAsync(cancellationToken);
            if (currentState is null)
            {
                return DeleteServerInstanceStatus.NotFound;
            }

            if (ActiveStatuses.Contains(currentState.Status))
            {
                return DeleteServerInstanceStatus.Active;
            }

            if (currentState.HasCommandHistory)
            {
                return DeleteServerInstanceStatus.HasCommandHistory;
            }
        }

        return DeleteServerInstanceStatus.Active;
    }

    private IQueryable<ServerInstanceEntity> OwnedByUser(Guid userId) =>
        dbContext.ServerInstances.Where(serverInstance =>
            dbContext.Agents.Any(agent =>
                agent.Id == serverInstance.AgentId && agent.UserId == userId));

    private static IQueryable<ServerInstanceDetails> ProjectDetails(
        IQueryable<ServerInstanceEntity> query) =>
        query.Select(serverInstance => new ServerInstanceDetails(
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
            serverInstance.UpdatedAt));

    private static ServerInstanceDetails MapDetails(ServerInstanceEntity serverInstance) =>
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

    private sealed record ServerInstanceDeletionState(
        ServerInstanceStatus Status,
        bool HasCommandHistory);
}

using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ServerPilot.Application.ServerInstances;
using ServerPilot.Domain.Commands;
using ServerPilot.Domain.ServerInstances;
using ApplicationStateReportResult =
    ServerPilot.Application.ServerInstances.ServerInstanceStateReportResult;
using DomainStateReportResult =
    ServerPilot.Domain.ServerInstances.ServerInstanceStateReportResult;
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

    public Task<ServerInstanceAgentDetails?> FindAgentOwnedByUserAsync(
        Guid agentId,
        Guid userId,
        CancellationToken cancellationToken) =>
        dbContext.Agents
            .AsNoTracking()
            .Where(agent => agent.Id == agentId && agent.UserId == userId)
            .Select(agent => new ServerInstanceAgentDetails(agent.LastSeenAt))
            .SingleOrDefaultAsync(cancellationToken);

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
                serverInstance.Status,
                serverInstance.LastProcessId,
                serverInstance.LastProcessStartedAt,
                serverInstance.LastStatusReportedAt,
                false,
                dbContext.Agents
                    .Where(agent => agent.Id == serverInstance.AgentId)
                    .Select(agent => agent.LastSeenAt)
                    .Single(),
                serverInstance.CreatedAt,
                serverInstance.UpdatedAt))
            .ToArrayAsync(cancellationToken);

    public Task<ServerInstanceDetails?> FindOwnedAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken) =>
        ProjectDetails(OwnedByUser(userId).Where(serverInstance => serverInstance.Id == id))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<UpdateServerInstanceResult> UpdateOwnedAsync(
        Guid id,
        Guid userId,
        ServerInstanceConfiguration configuration,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await LockOwnedServerInstanceAsync(id, userId, cancellationToken))
        {
            return new UpdateServerInstanceResult(UpdateServerInstanceStatus.NotFound, null);
        }

        ServerInstanceEntity? serverInstance = await OwnedByUser(userId)
            .SingleOrDefaultAsync(serverInstance => serverInstance.Id == id, cancellationToken);
        if (serverInstance is null)
        {
            return new UpdateServerInstanceResult(UpdateServerInstanceStatus.NotFound, null);
        }

        bool processConfigurationChanged =
            !string.Equals(
                serverInstance.ExecutablePath,
                configuration.ExecutablePath,
                StringComparison.Ordinal) ||
            !string.Equals(
                serverInstance.Arguments,
                configuration.Arguments,
                StringComparison.Ordinal) ||
            !string.Equals(
                serverInstance.WorkingDirectory,
                configuration.WorkingDirectory,
                StringComparison.Ordinal) ||
            !string.Equals(
                serverInstance.ProcessName,
                configuration.ProcessName,
                StringComparison.Ordinal);
        if (processConfigurationChanged &&
            (serverInstance.IsActive || await dbContext.ServerCommands
                .AsNoTracking()
                .AnyAsync(
                    command =>
                        command.ServerInstanceId == id &&
                        (command.Status == ServerCommandStatus.Pending ||
                         command.Status == ServerCommandStatus.Claimed ||
                         command.Status == ServerCommandStatus.Running),
                    cancellationToken)))
        {
            return new UpdateServerInstanceResult(
                UpdateServerInstanceStatus.ActiveProcessOrCommand,
                null);
        }

        serverInstance.UpdateConfiguration(configuration, updatedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        DateTimeOffset? agentLastSeenAt = await dbContext.Agents
            .AsNoTracking()
            .Where(agent => agent.Id == serverInstance.AgentId)
            .Select(agent => agent.LastSeenAt)
            .SingleAsync(cancellationToken);
        return new UpdateServerInstanceResult(
            UpdateServerInstanceStatus.Succeeded,
            MapDetails(serverInstance, agentLastSeenAt));
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

    public async Task<IReadOnlyList<AssignedServerInstanceDetails>> ListAssignedAsync(
        Guid agentId,
        int skip,
        int limit,
        CancellationToken cancellationToken) =>
        await dbContext.ServerInstances
            .AsNoTracking()
            .Where(serverInstance => serverInstance.AgentId == agentId)
            .OrderBy(serverInstance => serverInstance.CreatedAt)
            .ThenBy(serverInstance => serverInstance.Id)
            .Skip(skip)
            .Take(limit)
            .Select(serverInstance => new AssignedServerInstanceDetails(
                serverInstance.Id,
                serverInstance.ExecutablePath,
                serverInstance.Arguments,
                serverInstance.WorkingDirectory,
                serverInstance.ProcessName,
                serverInstance.Status,
                serverInstance.LastProcessId,
                serverInstance.LastProcessStartedAt,
                serverInstance.LastStatusReportedAt))
            .ToArrayAsync(cancellationToken);

    public async Task<ApplicationStateReportResult> RecordProcessStateAsync(
            Guid agentId,
            Guid serverInstanceId,
            ServerInstanceStatus status,
            int? processId,
            DateTimeOffset? processStartedAt,
            DateTimeOffset reportedAt,
            CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await LockAssignedServerInstanceAsync(
                serverInstanceId,
                agentId,
                cancellationToken))
        {
            return ApplicationStateReportResult.NotFound;
        }

        ServerInstanceEntity? serverInstance = await dbContext.ServerInstances
            .SingleOrDefaultAsync(
                item => item.Id == serverInstanceId && item.AgentId == agentId,
                cancellationToken);
        if (serverInstance is null)
        {
            return ApplicationStateReportResult.NotFound;
        }

        DomainStateReportResult result =
            serverInstance.RecordProcessState(
                status,
                processId,
                processStartedAt,
                reportedAt);
        ApplicationStateReportResult mapped = result switch
        {
            DomainStateReportResult.Succeeded => ApplicationStateReportResult.Succeeded,
            DomainStateReportResult.AlreadyApplied => ApplicationStateReportResult.AlreadyApplied,
            DomainStateReportResult.InvalidState => ApplicationStateReportResult.InvalidState,
            DomainStateReportResult.InvalidProcessIdentity =>
                ApplicationStateReportResult.InvalidProcessIdentity,
            DomainStateReportResult.StaleReport => ApplicationStateReportResult.StaleReport,
            _ => throw new InvalidOperationException(
                $"Unsupported process-state report result '{result}'."),
        };
        if (mapped == ApplicationStateReportResult.Succeeded)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return mapped;
    }

    private IQueryable<ServerInstanceEntity> OwnedByUser(Guid userId) =>
        dbContext.ServerInstances.Where(serverInstance =>
            dbContext.Agents.Any(agent =>
                agent.Id == serverInstance.AgentId && agent.UserId == userId));

    private async Task<bool> LockOwnedServerInstanceAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT server_instance.id
            FROM server_instances AS server_instance
            INNER JOIN agents AS agent ON agent.id = server_instance.agent_id
            WHERE server_instance.id = @id AND agent.user_id = @user_id
            FOR UPDATE OF server_instance
            """;

        DbConnection connection = dbContext.Database.GetDbConnection();
        await using DbCommand command = connection.CreateCommand();
        command.Transaction = dbContext.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = sql;
        AddParameter(command, "id", DbType.Guid, id);
        AddParameter(command, "user_id", DbType.Guid, userId);
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null and not DBNull;
    }

    private async Task<bool> LockAssignedServerInstanceAsync(
        Guid id,
        Guid agentId,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT id
            FROM server_instances
            WHERE id = @id AND agent_id = @agent_id
            FOR UPDATE
            """;

        DbConnection connection = dbContext.Database.GetDbConnection();
        await using DbCommand command = connection.CreateCommand();
        command.Transaction = dbContext.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = sql;
        AddParameter(command, "id", DbType.Guid, id);
        AddParameter(command, "agent_id", DbType.Guid, agentId);
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null and not DBNull;
    }

    private static void AddParameter(
        DbCommand command,
        string name,
        DbType type,
        object value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private IQueryable<ServerInstanceDetails> ProjectDetails(
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
            serverInstance.Status,
            serverInstance.LastProcessId,
            serverInstance.LastProcessStartedAt,
            serverInstance.LastStatusReportedAt,
            false,
            dbContext.Agents
                .Where(agent => agent.Id == serverInstance.AgentId)
                .Select(agent => agent.LastSeenAt)
                .Single(),
            serverInstance.CreatedAt,
            serverInstance.UpdatedAt));

    private static ServerInstanceDetails MapDetails(
        ServerInstanceEntity serverInstance,
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

    private sealed record ServerInstanceDeletionState(
        ServerInstanceStatus Status,
        bool HasCommandHistory);
}

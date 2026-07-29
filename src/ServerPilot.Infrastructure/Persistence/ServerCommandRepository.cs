using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ServerPilot.Application.Commands;
using ServerPilot.Domain.Commands;
using ServerPilot.Domain.ServerInstances;
using ServerPilot.Infrastructure.Persistence.Configurations;

namespace ServerPilot.Infrastructure.Persistence;

internal sealed class ServerCommandRepository(ServerPilotDbContext dbContext)
    : IServerCommandRepository
{
    public async Task<ServerCommandDetails?> ClaimNextAsync(
        Guid agentId,
        DateTimeOffset claimedAt,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            WITH next_command AS
            (
                SELECT id
                FROM server_commands
                WHERE agent_id = @agent_id AND status = @pending_status
                ORDER BY created_at, id
                FOR UPDATE SKIP LOCKED
                LIMIT 1
            )
            UPDATE server_commands AS command
            SET status = @claimed_status,
                claimed_at = @claimed_at,
                attempt_count = command.attempt_count + 1
            FROM next_command
            WHERE command.id = next_command.id
            RETURNING command.id,
                      command.agent_id,
                      command.server_instance_id,
                      command.type,
                      command.created_at,
                      command.claimed_at,
                      command.attempt_count,
                      command.correlation_id
            """;

        DbConnection connection = dbContext.Database.GetDbConnection();
        bool closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using DbCommand command = connection.CreateCommand();
            command.CommandText = sql;
            AddParameter(command, "agent_id", DbType.Guid, agentId);
            AddParameter(
                command,
                "pending_status",
                DbType.Int32,
                (int)ServerCommandStatus.Pending);
            AddParameter(
                command,
                "claimed_status",
                DbType.Int32,
                (int)ServerCommandStatus.Claimed);
            AddParameter(
                command,
                "claimed_at",
                DbType.DateTimeOffset,
                claimedAt.ToUniversalTime());

            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new ServerCommandDetails(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                (ServerCommandType)reader.GetInt32(3),
                ServerCommandStatus.Claimed,
                GetUtcTimestamp(reader, 4),
                GetUtcTimestamp(reader, 5),
                null,
                null,
                null,
                reader.GetInt32(6),
                reader.GetGuid(7));
        }
        finally
        {
            if (closeConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    public async Task<AgentCommandTransitionStatus> StartAsync(
        Guid commandId,
        Guid agentId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        int updated = await dbContext.ServerCommands
            .Where(command =>
                command.Id == commandId &&
                command.AgentId == agentId &&
                command.Status == ServerCommandStatus.Claimed)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(command => command.Status, ServerCommandStatus.Running)
                    .SetProperty(command => command.StartedAt, startedAt.ToUniversalTime()),
                cancellationToken);
        if (updated == 1)
        {
            return AgentCommandTransitionStatus.Succeeded;
        }

        AgentCommandSnapshot? snapshot = await FindAgentCommandAsync(
            commandId,
            agentId,
            cancellationToken);
        if (snapshot is null)
        {
            return AgentCommandTransitionStatus.NotFound;
        }

        return snapshot.StartedAt.HasValue
            ? AgentCommandTransitionStatus.AlreadyApplied
            : AgentCommandTransitionStatus.InvalidState;
    }

    public async Task<AgentCommandTransitionStatus> CompleteAsync(
        Guid commandId,
        Guid agentId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        int updated = await dbContext.ServerCommands
            .Where(command =>
                command.Id == commandId &&
                command.AgentId == agentId &&
                command.Status == ServerCommandStatus.Running)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(command => command.Status, ServerCommandStatus.Completed)
                    .SetProperty(command => command.CompletedAt, completedAt.ToUniversalTime()),
                cancellationToken);
        if (updated == 1)
        {
            return AgentCommandTransitionStatus.Succeeded;
        }

        AgentCommandSnapshot? snapshot = await FindAgentCommandAsync(
            commandId,
            agentId,
            cancellationToken);
        if (snapshot is null)
        {
            return AgentCommandTransitionStatus.NotFound;
        }

        return snapshot.Status == ServerCommandStatus.Completed
            ? AgentCommandTransitionStatus.AlreadyApplied
            : AgentCommandTransitionStatus.InvalidState;
    }

    public async Task<AgentCommandTransitionStatus> FailAsync(
        Guid commandId,
        Guid agentId,
        DateTimeOffset completedAt,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        int updated = await dbContext.ServerCommands
            .Where(command =>
                command.Id == commandId &&
                command.AgentId == agentId &&
                command.Status == ServerCommandStatus.Running)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(command => command.Status, ServerCommandStatus.Failed)
                    .SetProperty(command => command.CompletedAt, completedAt.ToUniversalTime())
                    .SetProperty(command => command.ErrorCode, errorCode)
                    .SetProperty(command => command.ErrorMessage, errorMessage),
                cancellationToken);
        if (updated == 1)
        {
            return AgentCommandTransitionStatus.Succeeded;
        }

        AgentCommandSnapshot? snapshot = await FindAgentCommandAsync(
            commandId,
            agentId,
            cancellationToken);
        if (snapshot is null)
        {
            return AgentCommandTransitionStatus.NotFound;
        }

        return snapshot.Status == ServerCommandStatus.Failed &&
            string.Equals(snapshot.ErrorCode, errorCode, StringComparison.Ordinal) &&
            string.Equals(snapshot.ErrorMessage, errorMessage, StringComparison.Ordinal)
            ? AgentCommandTransitionStatus.AlreadyApplied
            : AgentCommandTransitionStatus.InvalidState;
    }

    public async Task<CreateServerCommandResult> CreateOwnedAsync(
        Guid serverInstanceId,
        Guid userId,
        ServerCommandType type,
        DateTimeOffset createdAt,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        Guid? agentId = await OwnedByUser(userId)
            .AsNoTracking()
            .Where(serverInstance => serverInstance.Id == serverInstanceId)
            .Select(serverInstance => (Guid?)serverInstance.AgentId)
            .SingleOrDefaultAsync(cancellationToken);
        if (agentId is null)
        {
            return new CreateServerCommandResult(
                CreateServerCommandStatus.ServerInstanceNotFound,
                null);
        }

        ServerCommand command = ServerCommand.Create(
            Guid.NewGuid(),
            agentId.Value,
            serverInstanceId,
            type,
            createdAt,
            correlationId);
        dbContext.ServerCommands.Add(command);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsActiveCommandConflict(exception))
        {
            dbContext.ChangeTracker.Clear();
            return new CreateServerCommandResult(
                CreateServerCommandStatus.ActiveCommandConflict,
                null);
        }
        catch (DbUpdateException exception) when (IsServerInstanceDeletionRace(exception))
        {
            dbContext.ChangeTracker.Clear();
            return new CreateServerCommandResult(
                CreateServerCommandStatus.ServerInstanceNotFound,
                null);
        }

        return new CreateServerCommandResult(
            CreateServerCommandStatus.Succeeded,
            MapDetails(command));
    }

    public async Task<ServerCommandHistoryResult> ListOwnedAsync(
        Guid serverInstanceId,
        Guid userId,
        int skip,
        int limit,
        CancellationToken cancellationToken)
    {
        bool serverInstanceFound = await OwnedByUser(userId)
            .AsNoTracking()
            .AnyAsync(serverInstance => serverInstance.Id == serverInstanceId, cancellationToken);
        if (!serverInstanceFound)
        {
            return new ServerCommandHistoryResult(false, []);
        }

        ServerCommandDetails[] commands = await dbContext.ServerCommands
            .AsNoTracking()
            .Where(command => command.ServerInstanceId == serverInstanceId)
            .OrderByDescending(command => command.CreatedAt)
            .ThenByDescending(command => command.Id)
            .Skip(skip)
            .Take(limit)
            .Select(command => new ServerCommandDetails(
                command.Id,
                command.AgentId,
                command.ServerInstanceId,
                command.Type,
                command.Status,
                command.CreatedAt,
                command.ClaimedAt,
                command.StartedAt,
                command.CompletedAt,
                command.ErrorCode,
                command.AttemptCount,
                command.CorrelationId))
            .ToArrayAsync(cancellationToken);

        return new ServerCommandHistoryResult(true, commands);
    }

    private IQueryable<ServerInstance> OwnedByUser(Guid userId) =>
        dbContext.ServerInstances.Where(serverInstance =>
            dbContext.Agents.Any(agent =>
                agent.Id == serverInstance.AgentId && agent.UserId == userId));

    private Task<AgentCommandSnapshot?> FindAgentCommandAsync(
        Guid commandId,
        Guid agentId,
        CancellationToken cancellationToken) =>
        dbContext.ServerCommands
            .AsNoTracking()
            .Where(command => command.Id == commandId && command.AgentId == agentId)
            .Select(command => new AgentCommandSnapshot(
                command.Status,
                command.StartedAt,
                command.ErrorCode,
                command.ErrorMessage))
            .SingleOrDefaultAsync(cancellationToken);

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

    private static DateTimeOffset GetUtcTimestamp(DbDataReader reader, int ordinal) =>
        new(reader.GetFieldValue<DateTime>(ordinal).ToUniversalTime());

    private static bool IsActiveCommandConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: "23505",
            ConstraintName: ServerCommandConfiguration.ActiveServerInstanceIndexName,
        };

    private static bool IsServerInstanceDeletionRace(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: "23503",
            ConstraintName: "fk_server_commands_server_instances_agent_id_server_instance_id",
        };

    private static ServerCommandDetails MapDetails(ServerCommand command) =>
        new(
            command.Id,
            command.AgentId,
            command.ServerInstanceId,
            command.Type,
            command.Status,
            command.CreatedAt,
            command.ClaimedAt,
            command.StartedAt,
            command.CompletedAt,
            command.ErrorCode,
            command.AttemptCount,
            command.CorrelationId);

    private sealed record AgentCommandSnapshot(
        ServerCommandStatus Status,
        DateTimeOffset? StartedAt,
        string? ErrorCode,
        string? ErrorMessage);
}

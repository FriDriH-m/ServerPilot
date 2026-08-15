using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using ServerPilot.Application.Commands;
using ServerPilot.Domain.Commands;
using ServerPilot.Domain.ServerInstances;
using ServerPilot.Infrastructure.Persistence.Configurations;

namespace ServerPilot.Infrastructure.Persistence;

internal sealed class ServerCommandRepository(ServerPilotDbContext dbContext)
    : IServerCommandRepository
{
    public async Task<ClaimedServerCommandDetails?> ClaimNextAsync(
        Guid agentId,
        DateTimeOffset claimedAt,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            WITH agent_lock AS MATERIALIZED
            (
                SELECT id
                FROM agents
                WHERE id = @agent_id
                FOR UPDATE
            ),
            active_command AS MATERIALIZED
            (
                SELECT command.id,
                       command.agent_id,
                       command.server_instance_id,
                       command.type,
                       command.status,
                       command.created_at,
                       command.claimed_at,
                       command.started_at,
                       command.completed_at,
                       command.error_code,
                       command.attempt_count,
                       command.correlation_id
                FROM server_commands AS command
                INNER JOIN agent_lock ON agent_lock.id = command.agent_id
                WHERE command.status IN (@claimed_status, @running_status)
                ORDER BY command.claimed_at, command.id
                LIMIT 1
            ),
            next_command AS
            (
                SELECT command.id
                FROM server_commands AS command
                INNER JOIN agent_lock ON agent_lock.id = command.agent_id
                WHERE command.status = @pending_status
                  AND NOT EXISTS (SELECT 1 FROM active_command)
                ORDER BY command.created_at, command.id
                FOR UPDATE OF command SKIP LOCKED
                LIMIT 1
            ),
            claimed_command AS
            (
                UPDATE server_commands AS command
                SET status = @claimed_status,
                    claimed_at = GREATEST(@claimed_at, command.created_at),
                    attempt_count = command.attempt_count + 1
                FROM next_command
                WHERE command.id = next_command.id
                RETURNING command.id,
                          command.agent_id,
                          command.server_instance_id,
                          command.type,
                          command.status,
                          command.created_at,
                          command.claimed_at,
                          command.started_at,
                          command.completed_at,
                          command.error_code,
                          command.attempt_count,
                          command.correlation_id
            ),
            delivered_command AS
            (
                SELECT active_command.id,
                       active_command.agent_id,
                       active_command.server_instance_id,
                       active_command.type,
                       active_command.status,
                       active_command.created_at,
                       active_command.claimed_at,
                       active_command.started_at,
                       active_command.completed_at,
                       active_command.error_code,
                       active_command.attempt_count,
                       active_command.correlation_id,
                       TRUE AS is_recovery
                FROM active_command
                UNION ALL
                SELECT claimed_command.id,
                       claimed_command.agent_id,
                       claimed_command.server_instance_id,
                       claimed_command.type,
                       claimed_command.status,
                       claimed_command.created_at,
                       claimed_command.claimed_at,
                       claimed_command.started_at,
                       claimed_command.completed_at,
                       claimed_command.error_code,
                       claimed_command.attempt_count,
                       claimed_command.correlation_id,
                       FALSE AS is_recovery
                FROM claimed_command
                LIMIT 1
            )
            SELECT delivered_command.id,
                   delivered_command.agent_id,
                   delivered_command.server_instance_id,
                   delivered_command.type,
                   delivered_command.status,
                   delivered_command.created_at,
                   delivered_command.claimed_at,
                   delivered_command.started_at,
                   delivered_command.completed_at,
                   delivered_command.error_code,
                   delivered_command.attempt_count,
                   delivered_command.correlation_id,
                   delivered_command.is_recovery,
                   server_instance.profile,
                   server_instance.executable_path,
                   server_instance.arguments,
                   server_instance.working_directory,
                   server_instance.process_name,
                   server_instance.data_directory
            FROM delivered_command
            INNER JOIN server_instances AS server_instance
                ON server_instance.id = delivered_command.server_instance_id
               AND server_instance.agent_id = delivered_command.agent_id
            """;

        DbConnection connection = dbContext.Database.GetDbConnection();
        bool closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    return await ExecuteClaimAsync(
                        connection,
                        sql,
                        agentId,
                        claimedAt,
                        cancellationToken);
                }
                catch (PostgresException exception) when (
                    attempt == 0 && IsActiveAgentCommandConflict(exception))
                {
                    // A direct concurrent database writer may bypass the Agent row lock.
                    // Retry once so the named database invariant becomes a recovery read.
                }
            }

            throw new InvalidOperationException("Command claim retry did not produce a result.");
        }
        finally
        {
            if (closeConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task<ClaimedServerCommandDetails?> ExecuteClaimAsync(
        DbConnection connection,
        string sql,
        Guid agentId,
        DateTimeOffset claimedAt,
        CancellationToken cancellationToken)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "agent_id", DbType.Guid, agentId);
        AddParameter(command, "pending_status", DbType.Int32, (int)ServerCommandStatus.Pending);
        AddParameter(command, "claimed_status", DbType.Int32, (int)ServerCommandStatus.Claimed);
        AddParameter(command, "running_status", DbType.Int32, (int)ServerCommandStatus.Running);
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

        ServerCommandDetails details = new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            (ServerCommandType)reader.GetInt32(3),
            (ServerCommandStatus)reader.GetInt32(4),
            GetUtcTimestamp(reader, 5),
            GetNullableUtcTimestamp(reader, 6),
            GetNullableUtcTimestamp(reader, 7),
            GetNullableUtcTimestamp(reader, 8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.GetInt32(10),
            reader.GetGuid(11));
        AgentCommandDeliveryKind deliveryKind = reader.GetBoolean(12)
            ? AgentCommandDeliveryKind.Recovery
            : AgentCommandDeliveryKind.New;
        ServerInstanceExecutionDetails serverInstance = new(
            ((ServerInstanceProfile)reader.GetInt32(13)).ToString(),
            reader.GetString(14),
            reader.GetString(15),
            reader.GetString(16),
            reader.GetString(17),
            reader.IsDBNull(18) ? null : reader.GetString(18));
        return new ClaimedServerCommandDetails(details, deliveryKind, serverInstance);
    }

    public async Task<AgentCommandTransitionStatus> StartAsync(
        Guid commandId,
        Guid agentId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        DateTimeOffset utcStartedAt = startedAt.ToUniversalTime();
        int updated = await dbContext.ServerCommands
            .Where(command =>
                command.Id == commandId &&
                command.AgentId == agentId &&
                command.Status == ServerCommandStatus.Claimed &&
                command.ClaimedAt != null &&
                command.ClaimedAt <= utcStartedAt)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(command => command.Status, ServerCommandStatus.Running)
                    .SetProperty(command => command.StartedAt, utcStartedAt),
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
        DateTimeOffset utcCompletedAt = completedAt.ToUniversalTime();
        int updated = await dbContext.ServerCommands
            .Where(command =>
                command.Id == commandId &&
                command.AgentId == agentId &&
                command.Status == ServerCommandStatus.Running &&
                command.StartedAt != null &&
                command.StartedAt <= utcCompletedAt)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(command => command.Status, ServerCommandStatus.Completed)
                    .SetProperty(command => command.CompletedAt, utcCompletedAt),
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
        DateTimeOffset utcCompletedAt = completedAt.ToUniversalTime();
        int updated = await dbContext.ServerCommands
            .Where(command =>
                command.Id == commandId &&
                command.AgentId == agentId &&
                command.Status == ServerCommandStatus.Running &&
                command.StartedAt != null &&
                command.StartedAt <= utcCompletedAt)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(command => command.Status, ServerCommandStatus.Failed)
                    .SetProperty(command => command.CompletedAt, utcCompletedAt)
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
        await using IDbContextTransaction transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);
        Guid? agentId = await LockOwnedServerInstanceAsync(
            serverInstanceId,
            userId,
            cancellationToken);
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

        await transaction.CommitAsync(cancellationToken);

        return new CreateServerCommandResult(
            CreateServerCommandStatus.Succeeded,
            MapDetails(command));
    }

    public async Task<ServerCommandHistoryResult> ListOwnedAsync(
        Guid serverInstanceId,
        Guid userId,
        ServerCommandHistoryCursor? after,
        int limit,
        CancellationToken cancellationToken)
    {
        bool serverInstanceFound = await OwnedByUser(userId)
            .AsNoTracking()
            .AnyAsync(serverInstance => serverInstance.Id == serverInstanceId, cancellationToken);
        if (!serverInstanceFound)
        {
            return new ServerCommandHistoryResult(false, [], false);
        }

        IQueryable<ServerCommand> query = dbContext.ServerCommands
            .AsNoTracking()
            .Where(command => command.ServerInstanceId == serverInstanceId);
        if (after is not null)
        {
            query = query.Where(command =>
                command.CreatedAt < after.CreatedAt ||
                (command.CreatedAt == after.CreatedAt && command.Id.CompareTo(after.Id) < 0));
        }

        ServerCommandDetails[] commands = await query
            .OrderByDescending(command => command.CreatedAt)
            .ThenByDescending(command => command.Id)
            .Take(limit + 1)
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

        bool hasMore = commands.Length > limit;
        return new ServerCommandHistoryResult(true, commands.Take(limit).ToArray(), hasMore);
    }

    private IQueryable<ServerInstance> OwnedByUser(Guid userId) =>
        dbContext.ServerInstances.Where(serverInstance =>
            dbContext.Agents.Any(agent =>
                agent.Id == serverInstance.AgentId && agent.UserId == userId));

    private async Task<Guid?> LockOwnedServerInstanceAsync(
        Guid serverInstanceId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT server_instance.agent_id
            FROM server_instances AS server_instance
            INNER JOIN agents AS agent ON agent.id = server_instance.agent_id
            WHERE server_instance.id = @server_instance_id
              AND agent.user_id = @user_id
            FOR UPDATE OF server_instance
            """;

        DbConnection connection = dbContext.Database.GetDbConnection();
        await using DbCommand command = connection.CreateCommand();
        command.Transaction = dbContext.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = sql;
        AddParameter(command, "server_instance_id", DbType.Guid, serverInstanceId);
        AddParameter(command, "user_id", DbType.Guid, userId);
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is Guid agentId ? agentId : null;
    }

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

    private static DateTimeOffset? GetNullableUtcTimestamp(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : GetUtcTimestamp(reader, ordinal);

    private static bool IsActiveAgentCommandConflict(PostgresException exception) =>
        exception is
        {
            SqlState: "23505",
            ConstraintName: ServerCommandConfiguration.ActiveAgentIndexName,
        };

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

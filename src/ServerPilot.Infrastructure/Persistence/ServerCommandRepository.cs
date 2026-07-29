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
}

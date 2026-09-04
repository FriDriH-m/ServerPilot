using Microsoft.EntityFrameworkCore;
using ServerPilot.Application.Backups;
using ServerPilot.Application.Commands;
using ServerPilot.Domain.Backups;
using ServerPilot.Domain.Commands;

namespace ServerPilot.Infrastructure.Persistence;

internal sealed class BackupRepository(ServerPilotDbContext dbContext) : IBackupRepository
{
    public async Task<BackupPage> ListOwnedAsync(Guid serverInstanceId, Guid userId,
        ServerCommandHistoryCursor? after, int limit, CancellationToken cancellationToken)
    {
        bool found = await dbContext.ServerInstances.AsNoTracking().AnyAsync(instance =>
            instance.Id == serverInstanceId && dbContext.Agents.Any(agent =>
                agent.Id == instance.AgentId && agent.UserId == userId), cancellationToken);
        if (!found) return new BackupPage(false, [], false);

        var query = from command in dbContext.ServerCommands.AsNoTracking()
                    join backup in dbContext.Backups.AsNoTracking() on command.Id equals backup.Id
                    where command.ServerInstanceId == serverInstanceId
                    select new { command, backup };
        if (after is not null)
        {
            query = query.Where(item => item.command.CreatedAt < after.CreatedAt ||
                (item.command.CreatedAt == after.CreatedAt && item.command.Id.CompareTo(after.Id) < 0));
        }

        var rows = await query.OrderByDescending(item => item.command.CreatedAt)
            .ThenByDescending(item => item.command.Id).Take(limit + 1).Select(item => new BackupDetails(
            item.backup.Id, item.backup.Status.ToString(), item.command.CreatedAt,
            item.command.StartedAt, item.command.CompletedAt, item.backup.SizeBytes,
            item.backup.Checksum, item.command.ErrorCode)).ToArrayAsync(cancellationToken);
        return new BackupPage(true, rows.Take(limit).ToArray(), rows.Length > limit);
    }

    public async Task<AgentCommandTransitionStatus> CompleteAsync(Guid commandId, Guid agentId,
        long sizeBytes, string checksum, DateTimeOffset completedAt, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        ServerCommand? command = await dbContext.ServerCommands.FromSqlInterpolated(
            $"SELECT * FROM server_commands WHERE id = {commandId} AND agent_id = {agentId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (command is null || command.Type != ServerCommandType.CreateBackup)
            return AgentCommandTransitionStatus.NotFound;
        Backup? backup = await dbContext.Backups.SingleOrDefaultAsync(item => item.Id == commandId, cancellationToken);
        if (backup is null) return AgentCommandTransitionStatus.InvalidState;
        if (command.Status == ServerCommandStatus.Completed)
        {
            return backup.Status == BackupStatus.Completed && backup.SizeBytes == sizeBytes && backup.Checksum == checksum
                ? AgentCommandTransitionStatus.AlreadyApplied : AgentCommandTransitionStatus.InvalidState;
        }

        if (command.Status != ServerCommandStatus.Running || completedAt < command.StartedAt ||
            !backup.TryComplete(sizeBytes, checksum) || !command.TryComplete(completedAt))
            return AgentCommandTransitionStatus.InvalidState;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AgentCommandTransitionStatus.Succeeded;
    }
}

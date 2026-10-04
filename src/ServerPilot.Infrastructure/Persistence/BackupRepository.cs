using Microsoft.EntityFrameworkCore;
using ServerPilot.Application.Backups;
using ServerPilot.Application.Commands;
using ServerPilot.Domain.Backups;
using ServerPilot.Domain.Commands;

namespace ServerPilot.Infrastructure.Persistence;

internal sealed class BackupRepository(ServerPilotDbContext dbContext,
    ServerPilot.Application.Agents.AgentAvailabilityOptions availability) : IBackupRepository
{
    public async Task<BackupOperationResult> CreateOperationAsync(Guid serverInstanceId, Guid userId,
        Guid? backupId, int keepCount, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var instance = await dbContext.ServerInstances.FromSqlInterpolated($"""
            SELECT s.* FROM server_instances s
            JOIN agents a ON a.id = s.agent_id
            WHERE s.id = {serverInstanceId} AND a.user_id = {userId} FOR UPDATE OF s
            """).SingleOrDefaultAsync(cancellationToken);
        if (instance is null) return new(404);
        var candidates = from backup in dbContext.Backups
                         join command in dbContext.ServerCommands on backup.Id equals command.Id
                         where command.ServerInstanceId == serverInstanceId &&
                             (backup.Status == BackupStatus.Completed || backup.Status == BackupStatus.Deleting)
                         orderby command.CreatedAt descending, command.Id descending
                         select backup;
        if (backupId.HasValue && !await candidates.AnyAsync(item => item.Id == backupId &&
            item.Status == BackupStatus.Completed, cancellationToken)) return new(404);
        var freshAfter = now - availability.OfflineThreshold;
        if (!Backup.CanCreate(instance) || instance.LastStatusReportedAt < freshAfter ||
            instance.LastStatusReportedAt is null || !await dbContext.Agents.AnyAsync(agent =>
                agent.Id == instance.AgentId && agent.LastSeenAt >= freshAfter, cancellationToken)) return new(409);
        if (await dbContext.ServerCommands.AnyAsync(command => command.ServerInstanceId == serverInstanceId &&
            (command.Status == ServerCommandStatus.Pending || command.Status == ServerCommandStatus.Claimed ||
             command.Status == ServerCommandStatus.Running), cancellationToken)) return new(409);

        Backup[] targets;
        if (backupId.HasValue)
            targets = await candidates.Where(item => item.Id == backupId).ToArrayAsync(cancellationToken);
        else
        {
            // Interrupted deletion plans are resumed before selecting more completed archives.
            var unfinished = await candidates.Where(item => item.Status == BackupStatus.Deleting)
                .Take(1000).ToArrayAsync(cancellationToken);
            var excess = await candidates.Where(item => item.Status == BackupStatus.Completed)
                .Skip(keepCount).Take(1000 - unfinished.Length).ToArrayAsync(cancellationToken);
            targets = [.. unfinished, .. excess];
        }
        if (targets.Length == 0) return new(204);
        var operation = ServerCommand.CreateBackupOperation(Guid.NewGuid(), instance.AgentId, instance.Id,
            backupId.HasValue ? ServerCommandType.RestoreBackup : ServerCommandType.PruneBackups,
            now, Guid.NewGuid(), targets.Select(item => new BackupReference(item.Id, item.SizeBytes!.Value, item.Checksum!)).ToArray());
        dbContext.ServerCommands.Add(operation);
        if (!backupId.HasValue)
        {
            var ids = targets.Select(item => item.Id).ToArray();
            await dbContext.Backups.Where(item => ids.Contains(item.Id))
                .ExecuteUpdateAsync(update => update.SetProperty(item => item.Status, BackupStatus.Deleting), cancellationToken);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(201, operation.Id);
    }

    public async Task<AgentCommandTransitionStatus> ConfirmDeletedAsync(Guid commandId, Guid agentId,
        Guid backupId, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var command = await dbContext.ServerCommands.FromSqlInterpolated(
            $"SELECT * FROM server_commands WHERE id = {commandId} AND agent_id = {agentId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (command is null || command.Type != ServerCommandType.PruneBackups ||
            !command.BackupTargets.Any(item => item.Id == backupId)) return AgentCommandTransitionStatus.NotFound;
        if (command.Status is not (ServerCommandStatus.Running or ServerCommandStatus.Completed))
            return AgentCommandTransitionStatus.InvalidState;
        var backup = await dbContext.Backups.SingleAsync(item => item.Id == backupId, cancellationToken);
        if (backup.Status == BackupStatus.Deleted) return AgentCommandTransitionStatus.AlreadyApplied;
        if (command.Status != ServerCommandStatus.Running || backup.Status != BackupStatus.Deleting)
            return AgentCommandTransitionStatus.InvalidState;
        await dbContext.Backups.Where(item => item.Id == backupId)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.Status, BackupStatus.Deleted), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AgentCommandTransitionStatus.Succeeded;
    }

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
            return backup.Status is BackupStatus.Completed or BackupStatus.Deleting or BackupStatus.Deleted &&
                backup.SizeBytes == sizeBytes && backup.Checksum == checksum
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

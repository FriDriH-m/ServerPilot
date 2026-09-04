using ServerPilot.Application.Commands;
using ServerPilot.Domain.Backups;

namespace ServerPilot.Application.Backups;

public sealed class BackupService(IBackupRepository backups, TimeProvider timeProvider)
{
    public Task<BackupPage> ListAsync(Guid serverInstanceId, Guid userId,
        ServerCommandHistoryCursor? after, int limit, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty) throw new ArgumentException("User ID is required.", nameof(userId));
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        return backups.ListOwnedAsync(serverInstanceId, userId, after, limit, cancellationToken);
    }

    public Task<AgentCommandTransitionStatus> CompleteAsync(Guid commandId, Guid agentId,
        long sizeBytes, string? checksum, CancellationToken cancellationToken)
    {
        if (agentId == Guid.Empty) throw new ArgumentException("Agent ID is required.", nameof(agentId));
        if (!Backup.IsValidArtifact(sizeBytes, checksum))
        {
            return Task.FromResult(AgentCommandTransitionStatus.InvalidFailureDetails);
        }

        return backups.CompleteAsync(commandId, agentId, sizeBytes, checksum!.ToUpperInvariant(),
            timeProvider.GetUtcNow(), cancellationToken);
    }
}

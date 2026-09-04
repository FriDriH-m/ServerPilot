using ServerPilot.Application.Commands;

namespace ServerPilot.Application.Backups;

public sealed record BackupDetails(
    Guid Id,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    long? SizeBytes,
    string? Checksum,
    string? ErrorCode);

public sealed record BackupPage(bool ServerInstanceFound, IReadOnlyList<BackupDetails> Items, bool HasMore);

public interface IBackupRepository
{
    Task<BackupPage> ListOwnedAsync(Guid serverInstanceId, Guid userId,
        ServerCommandHistoryCursor? after, int limit, CancellationToken cancellationToken);

    Task<AgentCommandTransitionStatus> CompleteAsync(Guid commandId, Guid agentId,
        long sizeBytes, string checksum, DateTimeOffset completedAt, CancellationToken cancellationToken);
}

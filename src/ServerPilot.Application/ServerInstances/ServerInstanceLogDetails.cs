using ServerPilot.Domain.ServerInstances;

namespace ServerPilot.Application.ServerInstances;

public sealed record ServerInstanceLogDetails(
    ServerInstanceProfile Profile,
    ServerInstanceLogStatus? Status,
    string? Content,
    Guid? StreamId,
    long? Offset,
    string? ChunkContent,
    long? ChunkFromOffset,
    bool ChunkReset,
    DateTimeOffset? ReportedAt,
    DateTimeOffset? AgentLastSeenAt);

public enum ServerInstanceLogViewStatus
{
    Unsupported = 0,
    Waiting,
    Available,
    Missing,
    Unavailable,
}

public sealed record ServerInstanceLogView(
    ServerInstanceLogViewStatus Status,
    string? Cursor,
    bool Reset,
    IReadOnlyList<string> Lines,
    DateTimeOffset? ReportedAt,
    bool IsStale);

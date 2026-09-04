namespace ServerPilot.Api.Contracts.ServerInstances;

public sealed record ServerInstanceLogsResponse(
    string Status,
    string? Cursor,
    bool Reset,
    IReadOnlyList<string> Lines,
    DateTimeOffset? ReportedAt,
    bool IsStale);

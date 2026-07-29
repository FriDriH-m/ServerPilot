namespace ServerPilot.Api.Contracts.ServerInstances;

public sealed record ServerInstanceListResponse(
    Guid Id,
    Guid AgentId,
    string Name,
    string Status,
    string ReportedStatus,
    int? LastProcessId,
    DateTimeOffset? LastProcessStartedAt,
    DateTimeOffset? LastStatusReportedAt,
    bool IsStateStale,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

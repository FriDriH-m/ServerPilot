namespace ServerPilot.Api.Contracts.ServerInstances;

public sealed record ServerInstanceResponse(
    Guid Id,
    Guid AgentId,
    string Profile,
    string Name,
    string ExecutablePath,
    string Arguments,
    string WorkingDirectory,
    string ProcessName,
    string? DataDirectory,
    ProjectZomboidPathsResponse? ProjectZomboidPaths,
    string Status,
    string ReportedStatus,
    int? LastProcessId,
    DateTimeOffset? LastProcessStartedAt,
    DateTimeOffset? LastStatusReportedAt,
    bool IsStateStale,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

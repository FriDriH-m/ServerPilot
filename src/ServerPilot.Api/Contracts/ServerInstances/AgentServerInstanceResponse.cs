namespace ServerPilot.Api.Contracts.ServerInstances;

public sealed record AgentServerInstanceResponse(
    Guid Id,
    string Profile,
    string ExecutablePath,
    string Arguments,
    string WorkingDirectory,
    string ProcessName,
    string? DataDirectory,
    string ReportedStatus,
    int? LastProcessId,
    DateTimeOffset? LastProcessStartedAt,
    DateTimeOffset? LastStatusReportedAt);

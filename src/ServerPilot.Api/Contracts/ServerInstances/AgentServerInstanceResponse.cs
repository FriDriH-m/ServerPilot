namespace ServerPilot.Api.Contracts.ServerInstances;

public sealed record AgentServerInstanceResponse(
    Guid Id,
    string ExecutablePath,
    string Arguments,
    string WorkingDirectory,
    string ProcessName,
    string ReportedStatus,
    int? LastProcessId,
    DateTimeOffset? LastProcessStartedAt,
    DateTimeOffset? LastStatusReportedAt);

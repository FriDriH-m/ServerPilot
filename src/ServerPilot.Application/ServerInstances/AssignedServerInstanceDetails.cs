using ServerPilot.Domain.ServerInstances;

namespace ServerPilot.Application.ServerInstances;

public sealed record AssignedServerInstanceDetails(
    Guid Id,
    string ExecutablePath,
    string Arguments,
    string WorkingDirectory,
    string ProcessName,
    ServerInstanceStatus ReportedStatus,
    int? LastProcessId,
    DateTimeOffset? LastProcessStartedAt,
    DateTimeOffset? LastStatusReportedAt);

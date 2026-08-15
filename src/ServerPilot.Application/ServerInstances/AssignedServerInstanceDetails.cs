using ServerPilot.Domain.ServerInstances;

namespace ServerPilot.Application.ServerInstances;

public sealed record AssignedServerInstanceDetails(
    Guid Id,
    ServerInstanceProfile Profile,
    string ExecutablePath,
    string Arguments,
    string WorkingDirectory,
    string ProcessName,
    string? DataDirectory,
    ServerInstanceStatus ReportedStatus,
    int? LastProcessId,
    DateTimeOffset? LastProcessStartedAt,
    DateTimeOffset? LastStatusReportedAt);

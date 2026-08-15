using ServerPilot.Domain.ServerInstances;

namespace ServerPilot.Application.ServerInstances;

public sealed record ServerInstanceDetails(
    Guid Id,
    Guid AgentId,
    ServerInstanceProfile Profile,
    string Name,
    string ExecutablePath,
    string Arguments,
    string WorkingDirectory,
    string ProcessName,
    string? DataDirectory,
    ServerInstanceStatus Status,
    ServerInstanceStatus ReportedStatus,
    int? LastProcessId,
    DateTimeOffset? LastProcessStartedAt,
    DateTimeOffset? LastStatusReportedAt,
    bool IsStateStale,
    DateTimeOffset? AgentLastSeenAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

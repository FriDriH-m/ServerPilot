using ServerPilot.Domain.ServerInstances;

namespace ServerPilot.Application.ServerInstances;

public sealed record ServerInstanceListItem(
    Guid Id,
    Guid AgentId,
    string Name,
    ServerInstanceStatus Status,
    ServerInstanceStatus ReportedStatus,
    int? LastProcessId,
    DateTimeOffset? LastProcessStartedAt,
    DateTimeOffset? LastStatusReportedAt,
    bool IsStateStale,
    DateTimeOffset? AgentLastSeenAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

using ServerPilot.Domain.ServerInstances;

namespace ServerPilot.Application.ServerInstances;

public sealed record ServerInstanceListItem(
    Guid Id,
    Guid AgentId,
    string Name,
    ServerInstanceStatus Status,
    int? LastProcessId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

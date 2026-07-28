using ServerPilot.Domain.ServerInstances;

namespace ServerPilot.Application.ServerInstances;

public sealed record ServerInstanceDetails(
    Guid Id,
    Guid AgentId,
    string Name,
    string ExecutablePath,
    string Arguments,
    string WorkingDirectory,
    string ProcessName,
    ServerInstanceStatus Status,
    int? LastProcessId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

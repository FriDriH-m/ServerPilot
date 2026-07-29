namespace ServerPilot.Api.Contracts.ServerInstances;

public sealed record ServerInstanceResponse(
    Guid Id,
    Guid AgentId,
    string Name,
    string ExecutablePath,
    string Arguments,
    string WorkingDirectory,
    string ProcessName,
    string Status,
    int? LastProcessId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

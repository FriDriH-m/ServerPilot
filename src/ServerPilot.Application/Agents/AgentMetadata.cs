namespace ServerPilot.Application.Agents;

public sealed record AgentMetadata(
    Guid Id,
    string Name,
    string MachineName,
    string OperatingSystem,
    string Version,
    DateTimeOffset RegisteredAt,
    DateTimeOffset? LastSeenAt);

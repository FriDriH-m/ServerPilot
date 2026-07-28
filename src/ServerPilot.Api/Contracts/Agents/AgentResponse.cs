namespace ServerPilot.Api.Contracts.Agents;

public sealed record AgentResponse(
    Guid Id,
    string Name,
    string MachineName,
    string OperatingSystem,
    string Version,
    DateTimeOffset RegisteredAt,
    DateTimeOffset? LastSeenAt,
    string Status);

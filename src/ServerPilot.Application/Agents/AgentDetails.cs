namespace ServerPilot.Application.Agents;

public sealed record AgentDetails(
    Guid Id,
    string Name,
    string MachineName,
    string OperatingSystem,
    string Version,
    DateTimeOffset RegisteredAt,
    DateTimeOffset? LastSeenAt,
    AgentAvailabilityStatus Status);

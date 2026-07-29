namespace ServerPilot.Api.Contracts.ServerInstances;

public sealed record ServerInstanceListResponse(
    Guid Id,
    Guid AgentId,
    string Name,
    string Status,
    int? LastProcessId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

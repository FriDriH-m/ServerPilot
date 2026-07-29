namespace ServerPilot.Api.Contracts.Commands;

public sealed record AgentServerCommandResponse(
    Guid Id,
    Guid AgentId,
    Guid ServerInstanceId,
    string Type,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClaimedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? ErrorCode,
    int AttemptCount,
    Guid CorrelationId,
    string DeliveryKind,
    AgentServerInstanceConfigurationResponse ServerInstance);

public sealed record AgentServerInstanceConfigurationResponse(
    string ExecutablePath,
    string Arguments,
    string WorkingDirectory,
    string ProcessName);

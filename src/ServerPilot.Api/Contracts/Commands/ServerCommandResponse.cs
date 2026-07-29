namespace ServerPilot.Api.Contracts.Commands;

public sealed record ServerCommandResponse(
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
    Guid CorrelationId);

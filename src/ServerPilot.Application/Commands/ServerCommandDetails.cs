using ServerPilot.Domain.Commands;

namespace ServerPilot.Application.Commands;

public sealed record ServerCommandDetails(
    Guid Id,
    Guid AgentId,
    Guid ServerInstanceId,
    ServerCommandType Type,
    ServerCommandStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClaimedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? ErrorCode,
    int AttemptCount,
    Guid CorrelationId);

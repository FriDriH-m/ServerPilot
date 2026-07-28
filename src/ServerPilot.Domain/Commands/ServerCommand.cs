namespace ServerPilot.Domain.Commands;

public sealed class ServerCommand
{
    public const int MaximumErrorCodeLength = 64;
    public const int MaximumErrorMessageLength = 2_048;

    public ServerCommand(
        Guid id,
        Guid agentId,
        Guid serverInstanceId,
        ServerCommandType type,
        DateTimeOffset createdAt,
        Guid correlationId)
    {
        Id = RequireIdentifier(id, nameof(id));
        AgentId = RequireIdentifier(agentId, nameof(agentId));
        ServerInstanceId = RequireIdentifier(serverInstanceId, nameof(serverInstanceId));
        CorrelationId = RequireIdentifier(correlationId, nameof(correlationId));
        Type = Enum.IsDefined(type)
            ? type
            : throw new ArgumentOutOfRangeException(
                nameof(type),
                type,
                "Unsupported server command type.");
        CreatedAt = createdAt.ToUniversalTime();
        Status = ServerCommandStatus.Pending;
    }

    public Guid Id { get; }
    public Guid AgentId { get; }
    public Guid ServerInstanceId { get; }
    public ServerCommandType Type { get; }
    public ServerCommandStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? ClaimedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public int AttemptCount { get; private set; }
    public Guid CorrelationId { get; }

    public bool TryClaim(DateTimeOffset claimedAt)
    {
        if (Status != ServerCommandStatus.Pending)
        {
            return false;
        }

        ClaimedAt = NormalizeTransitionTime(claimedAt, CreatedAt, nameof(claimedAt));
        AttemptCount++;
        Status = ServerCommandStatus.Claimed;
        return true;
    }

    public bool TryStart(DateTimeOffset startedAt)
    {
        if (Status != ServerCommandStatus.Claimed)
        {
            return false;
        }

        StartedAt = NormalizeTransitionTime(startedAt, ClaimedAt!.Value, nameof(startedAt));
        Status = ServerCommandStatus.Running;
        return true;
    }

    public bool TryComplete(DateTimeOffset completedAt)
    {
        if (Status != ServerCommandStatus.Running)
        {
            return false;
        }

        CompletedAt = NormalizeTransitionTime(completedAt, StartedAt!.Value, nameof(completedAt));
        Status = ServerCommandStatus.Completed;
        return true;
    }

    public bool TryFail(DateTimeOffset completedAt, string errorCode, string errorMessage)
    {
        if (Status != ServerCommandStatus.Running)
        {
            return false;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        if (errorCode.Length > MaximumErrorCodeLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(errorCode),
                $"Error code must not exceed {MaximumErrorCodeLength} characters.");
        }

        if (errorMessage.Length > MaximumErrorMessageLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(errorMessage),
                $"Error message must not exceed {MaximumErrorMessageLength} characters.");
        }

        CompletedAt = NormalizeTransitionTime(completedAt, StartedAt!.Value, nameof(completedAt));
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Status = ServerCommandStatus.Failed;
        return true;
    }

    private static Guid RequireIdentifier(Guid value, string parameterName)
    {
        return value != Guid.Empty
            ? value
            : throw new ArgumentException("Identifier must not be empty.", parameterName);
    }

    private static DateTimeOffset NormalizeTransitionTime(
        DateTimeOffset value,
        DateTimeOffset earliestAllowed,
        string parameterName)
    {
        DateTimeOffset utcValue = value.ToUniversalTime();
        if (utcValue < earliestAllowed)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Transition time must not precede the previous state change.");
        }

        return utcValue;
    }
}

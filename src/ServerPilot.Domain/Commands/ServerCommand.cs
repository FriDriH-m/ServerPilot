namespace ServerPilot.Domain.Commands;

public sealed class ServerCommand
{
    public const int MaximumErrorCodeLength = 64;
    public const int MaximumErrorMessageLength = 2_048;

    private ServerCommand()
    {
    }

    private ServerCommand(
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

    public Guid Id { get; private set; }
    public Guid AgentId { get; private set; }
    public Guid ServerInstanceId { get; private set; }
    public ServerCommandType Type { get; private set; }
    public ServerCommandStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ClaimedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public int AttemptCount { get; private set; }
    public Guid CorrelationId { get; private set; }

    public static ServerCommand Create(
        Guid id,
        Guid agentId,
        Guid serverInstanceId,
        ServerCommandType type,
        DateTimeOffset createdAt,
        Guid correlationId) =>
        new(id, agentId, serverInstanceId, type, createdAt, correlationId);

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

        string normalizedErrorCode = NormalizeFailureDetail(
            errorCode,
            MaximumErrorCodeLength,
            nameof(errorCode));
        string normalizedErrorMessage = NormalizeFailureDetail(
            errorMessage,
            MaximumErrorMessageLength,
            nameof(errorMessage));
        CompletedAt = NormalizeTransitionTime(completedAt, StartedAt!.Value, nameof(completedAt));
        ErrorCode = normalizedErrorCode;
        ErrorMessage = normalizedErrorMessage;
        Status = ServerCommandStatus.Failed;
        return true;
    }

    public bool TryCancel(DateTimeOffset cancelledAt)
    {
        if (Status != ServerCommandStatus.Pending)
        {
            return false;
        }

        CompletedAt = NormalizeTransitionTime(cancelledAt, CreatedAt, nameof(cancelledAt));
        Status = ServerCommandStatus.Cancelled;
        return true;
    }

    public bool TryTimeout(DateTimeOffset timedOutAt)
    {
        DateTimeOffset? earliestAllowed = Status switch
        {
            ServerCommandStatus.Pending => CreatedAt,
            ServerCommandStatus.Claimed => ClaimedAt,
            ServerCommandStatus.Running => StartedAt,
            _ => null,
        };

        if (!earliestAllowed.HasValue)
        {
            return false;
        }

        CompletedAt = NormalizeTransitionTime(
            timedOutAt,
            earliestAllowed.Value,
            nameof(timedOutAt));
        Status = ServerCommandStatus.TimedOut;
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

    private static string NormalizeFailureDetail(
        string value,
        int maximumLength,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        string normalizedValue = value.Trim();
        if (normalizedValue.Length > maximumLength)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"{parameterName} must not exceed {maximumLength} characters.");
        }

        return normalizedValue;
    }
}

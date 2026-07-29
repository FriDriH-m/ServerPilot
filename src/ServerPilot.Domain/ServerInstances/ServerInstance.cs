namespace ServerPilot.Domain.ServerInstances;

public sealed class ServerInstance
{
    private ServerInstance()
    {
    }

    private ServerInstance(
        Guid id,
        Guid agentId,
        ServerInstanceConfiguration configuration,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Server instance ID cannot be empty.", nameof(id));
        }

        if (agentId == Guid.Empty)
        {
            throw new ArgumentException("Agent ID cannot be empty.", nameof(agentId));
        }

        ArgumentNullException.ThrowIfNull(configuration);

        Id = id;
        AgentId = agentId;
        ApplyConfiguration(configuration);
        CreatedAt = createdAt.ToUniversalTime();
        UpdatedAt = CreatedAt;
        Status = ServerInstanceStatus.Unknown;
    }

    public Guid Id { get; private set; }

    public Guid AgentId { get; private set; }

    public string Name { get; private set; } = null!;

    public string ExecutablePath { get; private set; } = null!;

    public string Arguments { get; private set; } = null!;

    public string WorkingDirectory { get; private set; } = null!;

    public string ProcessName { get; private set; } = null!;

    public ServerInstanceStatus Status { get; private set; }

    public int? LastProcessId { get; private set; }

    public DateTimeOffset? LastProcessStartedAt { get; private set; }

    public DateTimeOffset? LastStatusReportedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsActive => Status is
        ServerInstanceStatus.Starting or
        ServerInstanceStatus.Running or
        ServerInstanceStatus.Stopping;

    public static ServerInstance Create(
        Guid id,
        Guid agentId,
        ServerInstanceConfiguration configuration,
        DateTimeOffset createdAt) =>
        new(id, agentId, configuration, createdAt);

    public void UpdateConfiguration(
        ServerInstanceConfiguration configuration,
        DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        DateTimeOffset utcUpdatedAt = updatedAt.ToUniversalTime();
        if (utcUpdatedAt < UpdatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(updatedAt),
                "Server instance update cannot precede its current state.");
        }

        ApplyConfiguration(configuration);
        UpdatedAt = utcUpdatedAt;
    }

    public ServerInstanceStateReportResult RecordProcessState(
        ServerInstanceStatus status,
        int? lastProcessId,
        DateTimeOffset? lastProcessStartedAt,
        DateTimeOffset reportedAt)
    {
        if (!IsReportableStatus(status))
        {
            return ServerInstanceStateReportResult.InvalidState;
        }

        bool hasValidRunningIdentity =
            lastProcessId is > 0 && lastProcessStartedAt.HasValue;
        bool hasNoProcessIdentity =
            lastProcessId is null && lastProcessStartedAt is null;
        if ((status == ServerInstanceStatus.Running && !hasValidRunningIdentity) ||
            (status != ServerInstanceStatus.Running && !hasNoProcessIdentity))
        {
            return ServerInstanceStateReportResult.InvalidProcessIdentity;
        }

        DateTimeOffset utcReportedAt = reportedAt.ToUniversalTime();
        DateTimeOffset? utcProcessStartedAt = lastProcessStartedAt?.ToUniversalTime();
        if (utcReportedAt < CreatedAt ||
            (LastStatusReportedAt.HasValue && utcReportedAt < LastStatusReportedAt.Value))
        {
            return ServerInstanceStateReportResult.StaleReport;
        }

        if (LastStatusReportedAt == utcReportedAt)
        {
            return Status == status &&
                LastProcessId == lastProcessId &&
                LastProcessStartedAt == utcProcessStartedAt
                ? ServerInstanceStateReportResult.AlreadyApplied
                : ServerInstanceStateReportResult.StaleReport;
        }

        if (!CanTransition(Status, status))
        {
            return ServerInstanceStateReportResult.InvalidState;
        }

        Status = status;
        LastProcessId = lastProcessId;
        LastProcessStartedAt = utcProcessStartedAt;
        LastStatusReportedAt = utcReportedAt;
        if (utcReportedAt > UpdatedAt)
        {
            UpdatedAt = utcReportedAt;
        }

        return ServerInstanceStateReportResult.Succeeded;
    }

    private static bool IsReportableStatus(ServerInstanceStatus status) => status is
        ServerInstanceStatus.Running or
        ServerInstanceStatus.Stopped or
        ServerInstanceStatus.Crashed;

    private static bool CanTransition(
        ServerInstanceStatus current,
        ServerInstanceStatus next) => current switch
        {
            ServerInstanceStatus.Unknown => next is
                ServerInstanceStatus.Running or ServerInstanceStatus.Stopped,
            ServerInstanceStatus.Starting => next is
                ServerInstanceStatus.Running or ServerInstanceStatus.Crashed,
            ServerInstanceStatus.Running => next is
                ServerInstanceStatus.Running or
                ServerInstanceStatus.Stopped or
                ServerInstanceStatus.Crashed,
            ServerInstanceStatus.Stopping => next is
                ServerInstanceStatus.Stopped or ServerInstanceStatus.Crashed,
            ServerInstanceStatus.Stopped => next is
                ServerInstanceStatus.Stopped or ServerInstanceStatus.Running,
            ServerInstanceStatus.Crashed => next is
                ServerInstanceStatus.Crashed or
                ServerInstanceStatus.Stopped or
                ServerInstanceStatus.Running,
            _ => false,
        };

    private void ApplyConfiguration(ServerInstanceConfiguration configuration)
    {
        Name = configuration.Name;
        ExecutablePath = configuration.ExecutablePath;
        Arguments = configuration.Arguments;
        WorkingDirectory = configuration.WorkingDirectory;
        ProcessName = configuration.ProcessName;
    }
}

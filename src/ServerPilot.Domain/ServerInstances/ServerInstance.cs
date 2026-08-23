namespace ServerPilot.Domain.ServerInstances;

public sealed class ServerInstance
{
    private const long PersistedTimestampPrecisionTicks = TimeSpan.TicksPerMicrosecond;

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

    public ServerInstanceProfile Profile { get; private set; }

    public string Name { get; private set; } = null!;

    public string ExecutablePath { get; private set; } = null!;

    public string Arguments { get; private set; } = null!;

    public string WorkingDirectory { get; private set; } = null!;

    public string ProcessName { get; private set; } = null!;

    public string? DataDirectory { get; private set; }

    public ServerInstanceStatus Status { get; private set; }

    public int? LastProcessId { get; private set; }

    public DateTimeOffset? LastProcessStartedAt { get; private set; }

    public DateTimeOffset? LastStatusReportedAt { get; private set; }

    public double? LastCpuUsagePercent { get; private set; }

    public long? LastWorkingSetBytes { get; private set; }

    public long? LastUptimeSeconds { get; private set; }

    public DateTimeOffset? LastMetricsReportedAt { get; private set; }

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
        DateTimeOffset reportedAt,
        ServerInstanceMetricReport? metrics = null)
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

        if ((metrics is not null && status != ServerInstanceStatus.Running) ||
            (metrics is not null && !metrics.IsValid))
        {
            return ServerInstanceStateReportResult.InvalidMetrics;
        }

        DateTimeOffset utcReportedAt = reportedAt.ToUniversalTime();
        DateTimeOffset? utcProcessStartedAt = lastProcessStartedAt.HasValue
            ? NormalizePersistedTimestamp(lastProcessStartedAt.Value)
            : null;
        if (utcReportedAt < CreatedAt ||
            (LastStatusReportedAt.HasValue && utcReportedAt < LastStatusReportedAt.Value))
        {
            return ServerInstanceStateReportResult.StaleReport;
        }

        if (LastStatusReportedAt == utcReportedAt)
        {
            return Status == status &&
                LastProcessId == lastProcessId &&
                LastProcessStartedAt == utcProcessStartedAt &&
                MetricsMatch(metrics)
                ? ServerInstanceStateReportResult.AlreadyApplied
                : ServerInstanceStateReportResult.StaleReport;
        }

        if (!CanTransition(Status, status))
        {
            return ServerInstanceStateReportResult.InvalidState;
        }

        bool processIdentityChanged =
            LastProcessId != lastProcessId ||
            LastProcessStartedAt != utcProcessStartedAt;
        Status = status;
        LastProcessId = lastProcessId;
        LastProcessStartedAt = utcProcessStartedAt;
        LastStatusReportedAt = utcReportedAt;
        if (metrics is not null)
        {
            LastCpuUsagePercent = metrics.CpuUsagePercent;
            LastWorkingSetBytes = metrics.WorkingSetBytes;
            LastUptimeSeconds = metrics.UptimeSeconds;
            LastMetricsReportedAt = utcReportedAt;
        }
        else if (status != ServerInstanceStatus.Running || processIdentityChanged)
        {
            ClearMetrics();
        }

        if (utcReportedAt > UpdatedAt)
        {
            UpdatedAt = utcReportedAt;
        }

        return ServerInstanceStateReportResult.Succeeded;
    }

    private bool MetricsMatch(ServerInstanceMetricReport? metrics) => metrics is null
        ? true
        : LastCpuUsagePercent == metrics.CpuUsagePercent &&
          LastWorkingSetBytes == metrics.WorkingSetBytes &&
          LastUptimeSeconds == metrics.UptimeSeconds &&
          LastMetricsReportedAt == LastStatusReportedAt;

    private void ClearMetrics()
    {
        LastCpuUsagePercent = null;
        LastWorkingSetBytes = null;
        LastUptimeSeconds = null;
        LastMetricsReportedAt = null;
    }

    private static DateTimeOffset NormalizePersistedTimestamp(DateTimeOffset value)
    {
        long utcTicks = value.ToUniversalTime().Ticks;
        return new DateTimeOffset(
            utcTicks - utcTicks % PersistedTimestampPrecisionTicks,
            TimeSpan.Zero);
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
        Profile = configuration.Profile;
        Name = configuration.Name;
        ExecutablePath = configuration.ExecutablePath;
        Arguments = configuration.Arguments;
        WorkingDirectory = configuration.WorkingDirectory;
        ProcessName = configuration.ProcessName;
        DataDirectory = configuration.DataDirectory;
    }
}

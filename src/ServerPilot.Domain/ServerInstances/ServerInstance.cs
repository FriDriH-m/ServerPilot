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

    public void RecordProcessState(
        ServerInstanceStatus status,
        int? lastProcessId,
        DateTimeOffset updatedAt)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Unsupported server instance status.");
        }

        if (lastProcessId is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lastProcessId),
                "Process ID must be positive when specified.");
        }

        DateTimeOffset utcUpdatedAt = updatedAt.ToUniversalTime();
        if (utcUpdatedAt < UpdatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(updatedAt),
                "Server state cannot precede its current state.");
        }

        Status = status;
        LastProcessId = lastProcessId;
        UpdatedAt = utcUpdatedAt;
    }

    private void ApplyConfiguration(ServerInstanceConfiguration configuration)
    {
        Name = configuration.Name;
        ExecutablePath = configuration.ExecutablePath;
        Arguments = configuration.Arguments;
        WorkingDirectory = configuration.WorkingDirectory;
        ProcessName = configuration.ProcessName;
    }
}

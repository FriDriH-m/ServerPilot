using ServerPilot.Agent.Processes;

namespace ServerPilot.Agent.Api;

public enum AgentServerInstanceStatus
{
    Unknown = 1,
    Starting = 2,
    Running = 3,
    Stopping = 4,
    Stopped = 5,
    Crashed = 6,
}

public sealed record AssignedAgentServerInstance(
    Guid Id,
    string Profile,
    string ExecutablePath,
    string Arguments,
    string WorkingDirectory,
    string ProcessName,
    string? DataDirectory,
    AgentServerInstanceStatus ReportedStatus,
    ProcessIdentity? Identity,
    DateTimeOffset? LastStatusReportedAt,
    string? LogSourceIdentifier = null);

public enum AgentServerLogStatus
{
    Available = 1,
    Missing,
    Unavailable,
}

public sealed record AgentServerLogReport(
    AgentServerLogStatus Status,
    string SourceIdentifier,
    Guid? StreamId,
    long? FromOffset,
    long? ToOffset,
    bool Reset,
    string? Content);

public sealed record AgentProcessStateReport(
    AgentServerInstanceStatus Status,
    ProcessIdentity? Identity,
    ProcessMetricSample? Metrics,
    AgentServerLogReport? Log = null)
{
    public static AgentProcessStateReport Running(
        ProcessIdentity identity,
        ProcessMetricSample? metrics = null,
        AgentServerLogReport? log = null) =>
        new(AgentServerInstanceStatus.Running, identity, metrics, log);

    public static AgentProcessStateReport Stopped(AgentServerLogReport? log = null) =>
        new(AgentServerInstanceStatus.Stopped, null, null, log);

    public static AgentProcessStateReport Crashed(AgentServerLogReport? log = null) =>
        new(AgentServerInstanceStatus.Crashed, null, null, log);
}

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
    DateTimeOffset? LastStatusReportedAt);

public sealed record AgentProcessStateReport(
    AgentServerInstanceStatus Status,
    ProcessIdentity? Identity,
    ProcessMetricSample? Metrics)
{
    public static AgentProcessStateReport Running(
        ProcessIdentity identity,
        ProcessMetricSample? metrics = null) =>
        new(AgentServerInstanceStatus.Running, identity, metrics);

    public static AgentProcessStateReport Stopped() =>
        new(AgentServerInstanceStatus.Stopped, null, null);

    public static AgentProcessStateReport Crashed() =>
        new(AgentServerInstanceStatus.Crashed, null, null);
}

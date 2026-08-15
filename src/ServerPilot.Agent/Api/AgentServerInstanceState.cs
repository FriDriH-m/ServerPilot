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
    ProcessIdentity? Identity)
{
    public static AgentProcessStateReport Running(ProcessIdentity identity) =>
        new(AgentServerInstanceStatus.Running, identity);

    public static AgentProcessStateReport Stopped() =>
        new(AgentServerInstanceStatus.Stopped, null);

    public static AgentProcessStateReport Crashed() =>
        new(AgentServerInstanceStatus.Crashed, null);
}

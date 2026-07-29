namespace ServerPilot.Domain.ServerInstances;

public enum ServerInstanceStatus
{
    Unknown = 1,
    Starting = 2,
    Running = 3,
    Stopping = 4,
    Stopped = 5,
    Crashed = 6,
    Unreachable = 7,
}

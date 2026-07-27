namespace ServerPilot.Domain.Commands;

public enum ServerCommandStatus
{
    Pending = 1,
    Claimed = 2,
    Running = 3,
    Completed = 4,
    Failed = 5,
    Cancelled = 6,
    TimedOut = 7,
}

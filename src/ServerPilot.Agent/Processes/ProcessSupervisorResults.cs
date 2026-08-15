namespace ServerPilot.Agent.Processes;

public interface IProcessSupervisor
{
    Task<ProcessSupervisorResult> StartAsync(CancellationToken cancellationToken);

    Task<ProcessSupervisorResult> InspectAsync(CancellationToken cancellationToken);

    Task<ProcessSupervisorResult> StopAsync(CancellationToken cancellationToken);
}

public enum ProcessSupervisorStatus
{
    Started = 0,
    Running,
    Stopped,
    AlreadyRunning,
    AlreadyStopped,
    NotRunning,
    StaleProcessId,
    Failed,
}

public enum ProcessSupervisorFailure
{
    None = 0,
    ExecutableNotFound,
    ManagedExecutableNotFound,
    WorkingDirectoryNotFound,
    DataDirectoryNotFound,
    ProfileConfigurationNotFound,
    InvalidLauncher,
    StartFailed,
    InspectionFailed,
    AccessDenied,
    ForcedStopFailed,
    StopTimedOut,
}

public sealed record ProcessSupervisorResult(
    ProcessSupervisorStatus Status,
    ProcessIdentity? Identity = null,
    ProcessSupervisorFailure Failure = ProcessSupervisorFailure.None,
    bool Forced = false);

public sealed record ProcessStopTimeouts
{
    public ProcessStopTimeouts(TimeSpan gracefulStopTimeout, TimeSpan forcedStopTimeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(gracefulStopTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(forcedStopTimeout, TimeSpan.Zero);

        GracefulStopTimeout = gracefulStopTimeout;
        ForcedStopTimeout = forcedStopTimeout;
    }

    public TimeSpan GracefulStopTimeout { get; }

    public TimeSpan ForcedStopTimeout { get; }

    public static ProcessStopTimeouts Default { get; } = new(
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(5));

    public static ProcessStopTimeouts ProjectZomboid { get; } = new(
        TimeSpan.FromSeconds(60),
        TimeSpan.FromSeconds(10));
}

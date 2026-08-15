namespace ServerPilot.Agent.Processes;

public interface IProcessPlatform
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    Task<ProcessLaunchResult> LaunchAsync(
        LocalProcessConfiguration configuration,
        CancellationToken cancellationToken);

    ProcessLookupResult Lookup(ProcessIdentity identity);

    Task<ProcessSignalResult> RequestGracefulStopAsync(
        ProcessIdentity identity,
        CancellationToken cancellationToken);

    ProcessSignalResult ForceStop(ProcessIdentity identity);

    Task<ProcessWaitResult> WaitForExitAsync(
        ProcessIdentity identity,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

public enum ProcessPlatformStatus
{
    Succeeded = 0,
    NotFound,
    Exited,
    IdentityMismatch,
    InvalidConfiguration,
    NotSupported,
    AccessDenied,
    TimedOut,
    Failed,
}

public sealed record ProcessLaunchResult(
    ProcessPlatformStatus Status,
    ProcessIdentity? Identity = null);

public sealed record ProcessLookupResult(
    ProcessPlatformStatus Status,
    ProcessSnapshot? Snapshot = null);

public sealed record ProcessSignalResult(ProcessPlatformStatus Status);

public sealed record ProcessWaitResult(ProcessPlatformStatus Status);

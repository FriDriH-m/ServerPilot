namespace ServerPilot.Agent.Processes;

public interface IProcessPlatform
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    ProcessLaunchResult Launch(LocalProcessConfiguration configuration);

    ProcessLookupResult Lookup(int processId);

    ProcessSignalResult RequestGracefulStop(ProcessIdentity identity);

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

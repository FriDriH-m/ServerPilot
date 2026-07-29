using Microsoft.Extensions.Logging;

namespace ServerPilot.Agent.Processes;

public sealed class LocalProcessSupervisor : IProcessSupervisor, IDisposable
{
    private static readonly Action<ILogger, Guid, int, Exception?> LogProcessStarted =
        LoggerMessage.Define<Guid, int>(
            LogLevel.Information,
            new EventId(300, nameof(LogProcessStarted)),
            "ServerInstance {ServerInstanceId} started process {ProcessId}");

    private static readonly Action<ILogger, Guid, int, TimeSpan, Exception?> LogForcedTermination =
        LoggerMessage.Define<Guid, int, TimeSpan>(
            LogLevel.Warning,
            new EventId(301, nameof(LogForcedTermination)),
            "ServerInstance {ServerInstanceId} is forcing termination of process {ProcessId} after graceful timeout {GracefulStopTimeout}");

    private static readonly Action<ILogger, Guid, int, Exception?> LogStaleProcessId =
        LoggerMessage.Define<Guid, int>(
            LogLevel.Warning,
            new EventId(302, nameof(LogStaleProcessId)),
            "ServerInstance {ServerInstanceId} ignored stale process ID {ProcessId}");

    private readonly Guid _serverInstanceId;
    private readonly LocalProcessConfiguration _configuration;
    private readonly IProcessPlatform _platform;
    private readonly ProcessStopTimeouts _timeouts;
    private readonly ILogger<LocalProcessSupervisor> _logger;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private ProcessIdentity? _trackedIdentity;

    public LocalProcessSupervisor(
        Guid serverInstanceId,
        LocalProcessConfiguration configuration,
        IProcessPlatform platform,
        ILogger<LocalProcessSupervisor> logger,
        ProcessStopTimeouts? timeouts = null,
        ProcessIdentity? trackedIdentity = null)
    {
        if (serverInstanceId == Guid.Empty)
        {
            throw new ArgumentException("A server instance ID is required.", nameof(serverInstanceId));
        }

        _serverInstanceId = serverInstanceId;
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeouts = timeouts ?? ProcessStopTimeouts.Default;
        _trackedIdentity = trackedIdentity;
    }

    public async Task<ProcessSupervisorResult> StartAsync(CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            if (_trackedIdentity is not null)
            {
                ProcessSupervisorResult inspection = InspectTrackedProcess();
                if (inspection.Status == ProcessSupervisorStatus.Running)
                {
                    return inspection with { Status = ProcessSupervisorStatus.AlreadyRunning };
                }

                if (inspection.Status is ProcessSupervisorStatus.StaleProcessId or ProcessSupervisorStatus.Failed)
                {
                    return inspection;
                }
            }

            if (!_platform.FileExists(_configuration.ExecutablePath))
            {
                return Failed(ProcessSupervisorFailure.ExecutableNotFound);
            }

            if (!_platform.DirectoryExists(_configuration.WorkingDirectory))
            {
                return Failed(ProcessSupervisorFailure.WorkingDirectoryNotFound);
            }

            ProcessLaunchResult launch = _platform.Launch(_configuration);
            if (launch.Status != ProcessPlatformStatus.Succeeded || launch.Identity is null)
            {
                return Failed(MapFailure(launch.Status, ProcessSupervisorFailure.StartFailed));
            }

            _trackedIdentity = launch.Identity;
            LogProcessStarted(_logger, _serverInstanceId, launch.Identity.ProcessId, null);

            return new ProcessSupervisorResult(ProcessSupervisorStatus.Started, launch.Identity);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<ProcessSupervisorResult> InspectAsync(CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            return InspectTrackedProcess();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<ProcessSupervisorResult> StopAsync(CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            if (_trackedIdentity is null)
            {
                return new ProcessSupervisorResult(ProcessSupervisorStatus.AlreadyStopped);
            }

            ProcessIdentity identity = _trackedIdentity;
            ProcessSupervisorResult inspection = InspectTrackedProcess();
            if (inspection.Status == ProcessSupervisorStatus.NotRunning)
            {
                return new ProcessSupervisorResult(ProcessSupervisorStatus.AlreadyStopped);
            }

            if (inspection.Status is ProcessSupervisorStatus.StaleProcessId or ProcessSupervisorStatus.Failed)
            {
                return inspection;
            }

            ProcessSignalResult gracefulSignal = _platform.RequestGracefulStop(identity);
            if (gracefulSignal.Status == ProcessPlatformStatus.Succeeded)
            {
                ProcessWaitResult gracefulWait = await _platform.WaitForExitAsync(
                    identity,
                    _timeouts.GracefulStopTimeout,
                    cancellationToken);

                if (gracefulWait.Status == ProcessPlatformStatus.Exited)
                {
                    _trackedIdentity = null;
                    return new ProcessSupervisorResult(ProcessSupervisorStatus.Stopped, identity);
                }

                if (gracefulWait.Status == ProcessPlatformStatus.IdentityMismatch)
                {
                    return Stale(identity);
                }

                if (gracefulWait.Status == ProcessPlatformStatus.AccessDenied)
                {
                    return Failed(ProcessSupervisorFailure.AccessDenied, identity);
                }
            }
            else if (gracefulSignal.Status is ProcessPlatformStatus.NotFound or ProcessPlatformStatus.Exited)
            {
                _trackedIdentity = null;
                return new ProcessSupervisorResult(ProcessSupervisorStatus.Stopped, identity);
            }
            else if (gracefulSignal.Status == ProcessPlatformStatus.IdentityMismatch)
            {
                return Stale(identity);
            }
            else if (gracefulSignal.Status == ProcessPlatformStatus.AccessDenied)
            {
                return Failed(ProcessSupervisorFailure.AccessDenied, identity);
            }

            LogForcedTermination(
                _logger,
                _serverInstanceId,
                identity.ProcessId,
                _timeouts.GracefulStopTimeout,
                null);

            ProcessSignalResult forceSignal = _platform.ForceStop(identity);
            if (forceSignal.Status is ProcessPlatformStatus.NotFound or ProcessPlatformStatus.Exited)
            {
                _trackedIdentity = null;
                return new ProcessSupervisorResult(
                    ProcessSupervisorStatus.Stopped,
                    identity,
                    Forced: true);
            }

            if (forceSignal.Status == ProcessPlatformStatus.IdentityMismatch)
            {
                return Stale(identity);
            }

            if (forceSignal.Status != ProcessPlatformStatus.Succeeded)
            {
                return Failed(
                    MapFailure(forceSignal.Status, ProcessSupervisorFailure.ForcedStopFailed),
                    identity,
                    forced: true);
            }

            ProcessWaitResult forcedWait = await _platform.WaitForExitAsync(
                identity,
                _timeouts.ForcedStopTimeout,
                cancellationToken);
            if (forcedWait.Status == ProcessPlatformStatus.Exited)
            {
                _trackedIdentity = null;
                return new ProcessSupervisorResult(
                    ProcessSupervisorStatus.Stopped,
                    identity,
                    Forced: true);
            }

            if (forcedWait.Status == ProcessPlatformStatus.IdentityMismatch)
            {
                return Stale(identity);
            }

            ProcessSupervisorFailure forcedWaitFailure = forcedWait.Status switch
            {
                ProcessPlatformStatus.AccessDenied => ProcessSupervisorFailure.AccessDenied,
                ProcessPlatformStatus.TimedOut => ProcessSupervisorFailure.StopTimedOut,
                _ => ProcessSupervisorFailure.ForcedStopFailed,
            };
            return Failed(
                forcedWaitFailure,
                identity,
                forced: true);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public void Dispose() => _operationGate.Dispose();

    private ProcessSupervisorResult InspectTrackedProcess()
    {
        if (_trackedIdentity is null)
        {
            return new ProcessSupervisorResult(ProcessSupervisorStatus.NotRunning);
        }

        ProcessIdentity identity = _trackedIdentity;
        ProcessLookupResult lookup = _platform.Lookup(identity.ProcessId);
        if (lookup.Status is ProcessPlatformStatus.NotFound or ProcessPlatformStatus.Exited)
        {
            _trackedIdentity = null;
            return new ProcessSupervisorResult(ProcessSupervisorStatus.NotRunning);
        }

        if (lookup.Status == ProcessPlatformStatus.AccessDenied)
        {
            return Failed(ProcessSupervisorFailure.AccessDenied, identity);
        }

        if (lookup.Status != ProcessPlatformStatus.Succeeded || lookup.Snapshot is null)
        {
            return Failed(ProcessSupervisorFailure.InspectionFailed, identity);
        }

        if (!ProcessIdentityPolicy.Matches(identity, lookup.Snapshot))
        {
            return Stale(identity);
        }

        return new ProcessSupervisorResult(ProcessSupervisorStatus.Running, identity);
    }

    private ProcessSupervisorResult Stale(ProcessIdentity identity)
    {
        _trackedIdentity = null;
        LogStaleProcessId(_logger, _serverInstanceId, identity.ProcessId, null);

        return new ProcessSupervisorResult(ProcessSupervisorStatus.StaleProcessId, identity);
    }

    private static ProcessSupervisorResult Failed(
        ProcessSupervisorFailure failure,
        ProcessIdentity? identity = null,
        bool forced = false) =>
        new(ProcessSupervisorStatus.Failed, identity, failure, forced);

    private static ProcessSupervisorFailure MapFailure(
        ProcessPlatformStatus status,
        ProcessSupervisorFailure fallback) =>
        status == ProcessPlatformStatus.AccessDenied
            ? ProcessSupervisorFailure.AccessDenied
            : fallback;
}

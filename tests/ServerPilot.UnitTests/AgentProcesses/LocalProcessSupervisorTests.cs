using Microsoft.Extensions.Logging.Abstractions;
using ServerPilot.Agent.Processes;

namespace ServerPilot.UnitTests.AgentProcesses;

public sealed class LocalProcessSupervisorTests
{
    private static readonly DateTimeOffset StartedAt = new(
        2026,
        7,
        29,
        12,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public async Task MissingExecutableProducesTypedFailureWithoutLaunching()
    {
        FakeProcessPlatform platform = new() { ExecutableExists = false };
        using LocalProcessSupervisor supervisor = CreateSupervisor(platform);

        ProcessSupervisorResult result = await supervisor.StartAsync(CancellationToken.None);

        Assert.Equal(ProcessSupervisorStatus.Failed, result.Status);
        Assert.Equal(ProcessSupervisorFailure.ExecutableNotFound, result.Failure);
        Assert.Equal(0, platform.LaunchCalls);
    }

    [Fact]
    public async Task RepeatedStartDoesNotLaunchASecondProcess()
    {
        ProcessIdentity identity = CreateIdentity();
        FakeProcessPlatform platform = new()
        {
            LaunchIdentity = identity,
            CurrentSnapshot = CreateSnapshot(identity),
        };
        using LocalProcessSupervisor supervisor = CreateSupervisor(platform);

        ProcessSupervisorResult first = await supervisor.StartAsync(CancellationToken.None);
        ProcessSupervisorResult second = await supervisor.StartAsync(CancellationToken.None);

        Assert.Equal(ProcessSupervisorStatus.Started, first.Status);
        Assert.Equal(ProcessSupervisorStatus.AlreadyRunning, second.Status);
        Assert.Equal(1, platform.LaunchCalls);
    }

    [Fact]
    public async Task StaleProcessIdIsNeverSignalled()
    {
        ProcessIdentity identity = CreateIdentity();
        FakeProcessPlatform platform = new()
        {
            CurrentSnapshot = CreateSnapshot(identity) with
            {
                StartedAtUtc = identity.StartedAtUtc.AddSeconds(1),
            },
        };
        using LocalProcessSupervisor supervisor = CreateSupervisor(platform, identity);

        ProcessSupervisorResult result = await supervisor.StopAsync(CancellationToken.None);

        Assert.Equal(ProcessSupervisorStatus.StaleProcessId, result.Status);
        Assert.Equal(0, platform.GracefulStopCalls);
        Assert.Equal(0, platform.ForceStopCalls);
    }

    [Fact]
    public async Task StopWithoutTrackedProcessIsIdempotent()
    {
        FakeProcessPlatform platform = new();
        using LocalProcessSupervisor supervisor = CreateSupervisor(platform);

        ProcessSupervisorResult result = await supervisor.StopAsync(CancellationToken.None);

        Assert.Equal(ProcessSupervisorStatus.AlreadyStopped, result.Status);
        Assert.Equal(0, platform.GracefulStopCalls);
        Assert.Equal(0, platform.ForceStopCalls);
    }

    [Fact]
    public async Task GracefulStopDoesNotForceTermination()
    {
        ProcessIdentity identity = CreateIdentity();
        FakeProcessPlatform platform = new()
        {
            CurrentSnapshot = CreateSnapshot(identity),
            GracefulStopStatus = ProcessPlatformStatus.Succeeded,
        };
        platform.WaitResults.Enqueue(ProcessPlatformStatus.Exited);
        using LocalProcessSupervisor supervisor = CreateSupervisor(platform, identity);

        ProcessSupervisorResult result = await supervisor.StopAsync(CancellationToken.None);

        Assert.Equal(ProcessSupervisorStatus.Stopped, result.Status);
        Assert.False(result.Forced);
        Assert.Equal(1, platform.GracefulStopCalls);
        Assert.Equal(0, platform.ForceStopCalls);
    }

    [Fact]
    public async Task UnsupportedGracefulStopUsesBoundedForcedFallback()
    {
        ProcessIdentity identity = CreateIdentity();
        FakeProcessPlatform platform = new()
        {
            CurrentSnapshot = CreateSnapshot(identity),
            GracefulStopStatus = ProcessPlatformStatus.NotSupported,
            ForceStopStatus = ProcessPlatformStatus.Succeeded,
        };
        platform.WaitResults.Enqueue(ProcessPlatformStatus.Exited);
        using LocalProcessSupervisor supervisor = CreateSupervisor(platform, identity);

        ProcessSupervisorResult result = await supervisor.StopAsync(CancellationToken.None);

        Assert.Equal(ProcessSupervisorStatus.Stopped, result.Status);
        Assert.True(result.Forced);
        Assert.Equal(1, platform.ForceStopCalls);
        Assert.Single(platform.ObservedTimeouts);
        Assert.Equal(TimeSpan.FromSeconds(2), platform.ObservedTimeouts[0]);
    }

    private static LocalProcessSupervisor CreateSupervisor(
        FakeProcessPlatform platform,
        ProcessIdentity? identity = null)
    {
        LocalProcessConfigurationResult configuration = LocalProcessConfiguration.Create(
            @"C:\Servers\server.exe",
            "--port 27015",
            @"C:\Servers",
            "server");

        return new LocalProcessSupervisor(
            Guid.NewGuid(),
            configuration.Configuration!,
            platform,
            NullLogger<LocalProcessSupervisor>.Instance,
            new ProcessStopTimeouts(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)),
            identity);
    }

    private static ProcessIdentity CreateIdentity() =>
        new(42, StartedAt, @"C:\Servers\server.exe", "server");

    private static ProcessSnapshot CreateSnapshot(ProcessIdentity identity) =>
        new(
            identity.ProcessId,
            identity.StartedAtUtc,
            identity.ExecutablePath,
            identity.ProcessName);

    private sealed class FakeProcessPlatform : IProcessPlatform
    {
        public bool ExecutableExists { get; init; } = true;

        public bool WorkingDirectoryExists { get; init; } = true;

        public ProcessIdentity? LaunchIdentity { get; init; } = CreateIdentity();

        public ProcessSnapshot? CurrentSnapshot { get; init; }

        public ProcessPlatformStatus GracefulStopStatus { get; init; } =
            ProcessPlatformStatus.NotSupported;

        public ProcessPlatformStatus ForceStopStatus { get; init; } =
            ProcessPlatformStatus.Succeeded;

        public Queue<ProcessPlatformStatus> WaitResults { get; } = new();

        public List<TimeSpan> ObservedTimeouts { get; } = [];

        public int LaunchCalls { get; private set; }

        public int GracefulStopCalls { get; private set; }

        public int ForceStopCalls { get; private set; }

        public bool FileExists(string path) => ExecutableExists;

        public bool DirectoryExists(string path) => WorkingDirectoryExists;

        public ProcessLaunchResult Launch(LocalProcessConfiguration configuration)
        {
            LaunchCalls++;
            return new ProcessLaunchResult(ProcessPlatformStatus.Succeeded, LaunchIdentity);
        }

        public ProcessLookupResult Lookup(int processId) =>
            CurrentSnapshot is null
                ? new ProcessLookupResult(ProcessPlatformStatus.NotFound)
                : new ProcessLookupResult(ProcessPlatformStatus.Succeeded, CurrentSnapshot);

        public ProcessSignalResult RequestGracefulStop(ProcessIdentity identity)
        {
            GracefulStopCalls++;
            return new ProcessSignalResult(GracefulStopStatus);
        }

        public ProcessSignalResult ForceStop(ProcessIdentity identity)
        {
            ForceStopCalls++;
            return new ProcessSignalResult(ForceStopStatus);
        }

        public Task<ProcessWaitResult> WaitForExitAsync(
            ProcessIdentity identity,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            ObservedTimeouts.Add(timeout);
            ProcessPlatformStatus status = WaitResults.Count > 0
                ? WaitResults.Dequeue()
                : ProcessPlatformStatus.Failed;
            return Task.FromResult(new ProcessWaitResult(status));
        }
    }
}

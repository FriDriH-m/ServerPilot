using Microsoft.Extensions.Logging.Abstractions;
using ServerPilot.Agent.Processes;

namespace ServerPilot.UnitTests.AgentProcesses;

public sealed class ProcessSupervisorRegistryTests
{
    [Fact]
    public void ReusesSupervisorOnlyForUnchangedStoredConfiguration()
    {
        using LocalProcessSupervisorRegistry registry = new(
            new SystemProcessPlatform(),
            NullLoggerFactory.Instance);
        Guid serverInstanceId = Guid.NewGuid();
        ProcessSupervisorRequest request = CreateRequest();

        ProcessSupervisorResolution first = registry.Resolve(serverInstanceId, request);
        ProcessSupervisorResolution second = registry.Resolve(serverInstanceId, request);
        ProcessSupervisorResolution changed = registry.Resolve(
            serverInstanceId,
            request with { Arguments = "--different" });

        Assert.NotNull(first.Supervisor);
        Assert.Same(first.Supervisor, second.Supervisor);
        Assert.Null(changed.Supervisor);
        Assert.Equal(
            ProcessSupervisorResolutionFailure.ConfigurationChanged,
            changed.Failure);
    }

    [Fact]
    public void RejectsInvalidConfigurationBeforeCreatingSupervisor()
    {
        using LocalProcessSupervisorRegistry registry = new(
            new SystemProcessPlatform(),
            NullLoggerFactory.Instance);

        ProcessSupervisorResolution result = registry.Resolve(
            Guid.NewGuid(),
            CreateRequest() with { ExecutablePath = "server.exe" });

        Assert.Null(result.Supervisor);
        Assert.Equal(ProcessSupervisorResolutionFailure.InvalidConfiguration, result.Failure);
    }

    [Fact]
    public async Task SeedsPersistedIdentityForSafeRestartRediscovery()
    {
        ProcessIdentity identity = new(
            42,
            new DateTimeOffset(2026, 7, 29, 12, 0, 0, TimeSpan.Zero),
            @"C:\Servers\server.exe",
            "server");
        using LocalProcessSupervisorRegistry registry = new(
            new IdentityProcessPlatform(new ProcessSnapshot(
                identity.ProcessId,
                identity.StartedAtUtc,
                identity.ExecutablePath,
                identity.ProcessName)),
            NullLoggerFactory.Instance);

        ProcessSupervisorResolution resolution = registry.Resolve(
            Guid.NewGuid(),
            CreateRequest() with { TrackedIdentity = identity });
        ProcessSupervisorResult inspection = await resolution.Supervisor!.InspectAsync(
            CancellationToken.None);

        Assert.Equal(ProcessSupervisorStatus.Running, inspection.Status);
        Assert.Equal(identity, inspection.Identity);
    }

    private static ProcessSupervisorRequest CreateRequest() => new(
        "Generic",
        @"C:\Servers\server.exe",
        "--port 16261",
        @"C:\Servers",
        "server",
        null);

    private sealed class IdentityProcessPlatform(ProcessSnapshot snapshot) : IProcessPlatform
    {
        public bool FileExists(string path) => true;

        public bool DirectoryExists(string path) => true;

        public Task<ProcessLaunchResult> LaunchAsync(
            LocalProcessConfiguration configuration,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ProcessLaunchResult(ProcessPlatformStatus.Failed));

        public ProcessLookupResult Lookup(ProcessIdentity identity) =>
            new(ProcessPlatformStatus.Succeeded, snapshot);

        public Task<ProcessSignalResult> RequestGracefulStopAsync(
            ProcessIdentity identity,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ProcessSignalResult(ProcessPlatformStatus.NotSupported));

        public ProcessSignalResult ForceStop(ProcessIdentity identity) =>
            new(ProcessPlatformStatus.Failed);

        public Task<ProcessWaitResult> WaitForExitAsync(
            ProcessIdentity identity,
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ProcessWaitResult(ProcessPlatformStatus.Failed));
    }
}

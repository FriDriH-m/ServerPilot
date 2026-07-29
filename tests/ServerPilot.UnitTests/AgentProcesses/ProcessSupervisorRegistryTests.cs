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

    private static ProcessSupervisorRequest CreateRequest() => new(
        @"C:\Servers\server.exe",
        "--port 16261",
        @"C:\Servers",
        "server");
}

using Microsoft.Extensions.Logging;

namespace ServerPilot.Agent.Processes;

public interface IProcessSupervisorRegistry
{
    ProcessSupervisorResolution Resolve(
        Guid serverInstanceId,
        ProcessSupervisorRequest request);
}

public sealed record ProcessSupervisorRequest(
    string ExecutablePath,
    string Arguments,
    string WorkingDirectory,
    string ProcessName,
    ProcessIdentity? TrackedIdentity = null);

public enum ProcessSupervisorResolutionFailure
{
    None = 0,
    InvalidConfiguration,
    ConfigurationChanged,
}

public sealed record ProcessSupervisorResolution(
    IProcessSupervisor? Supervisor,
    ProcessSupervisorResolutionFailure Failure)
{
    public static ProcessSupervisorResolution Succeeded(IProcessSupervisor supervisor) =>
        new(supervisor, ProcessSupervisorResolutionFailure.None);

    public static ProcessSupervisorResolution Failed(
        ProcessSupervisorResolutionFailure failure) =>
        new(null, failure);
}

public sealed class LocalProcessSupervisorRegistry(
    IProcessPlatform platform,
    ILoggerFactory loggerFactory) : IProcessSupervisorRegistry, IDisposable
{
    private readonly Lock sync = new();
    private readonly Dictionary<Guid, RegistryEntry> entries = [];

    public ProcessSupervisorResolution Resolve(
        Guid serverInstanceId,
        ProcessSupervisorRequest request)
    {
        LocalProcessConfigurationResult configurationResult = LocalProcessConfiguration.Create(
            request.ExecutablePath,
            request.Arguments,
            request.WorkingDirectory,
            request.ProcessName);
        if (!configurationResult.IsValid || configurationResult.Configuration is null)
        {
            return ProcessSupervisorResolution.Failed(
                ProcessSupervisorResolutionFailure.InvalidConfiguration);
        }

        LocalProcessConfiguration configuration = configurationResult.Configuration;
        lock (sync)
        {
            if (entries.TryGetValue(serverInstanceId, out RegistryEntry? existing))
            {
                return existing.Configuration == configuration
                    ? ProcessSupervisorResolution.Succeeded(existing.Supervisor)
                    : ProcessSupervisorResolution.Failed(
                        ProcessSupervisorResolutionFailure.ConfigurationChanged);
            }

            LocalProcessSupervisor supervisor = new(
                serverInstanceId,
                configuration,
                platform,
                loggerFactory.CreateLogger<LocalProcessSupervisor>(),
                trackedIdentity: request.TrackedIdentity);
            entries.Add(serverInstanceId, new RegistryEntry(configuration, supervisor));
            return ProcessSupervisorResolution.Succeeded(supervisor);
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            foreach (RegistryEntry entry in entries.Values)
            {
                entry.Supervisor.Dispose();
            }

            entries.Clear();
        }
    }

    private sealed record RegistryEntry(
        LocalProcessConfiguration Configuration,
        LocalProcessSupervisor Supervisor);
}

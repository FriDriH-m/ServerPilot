using Microsoft.Extensions.Logging;
using ServerPilot.Agent.Api;
using ServerPilot.Agent.Credentials;
using ServerPilot.Agent.Looping;

namespace ServerPilot.Agent.Processes;

public interface IAgentProcessStateReconciler
{
    Task ReconcileAsync(
        AgentCredential credential,
        CancellationToken cancellationToken);
}

public sealed class AgentProcessStateReconciler(
    IAgentApiClient apiClient,
    AgentRetryExecutor retry,
    IProcessSupervisorRegistry supervisors,
    ProcessMetricsSampler metricsSampler,
    ILogger<AgentProcessStateReconciler> logger) : IAgentProcessStateReconciler
{
    private static readonly Action<ILogger, Guid, Guid, string, int?, Exception?>
        LogProcessState = LoggerMessage.Define<Guid, Guid, string, int?>(
            LogLevel.Debug,
            new EventId(500, nameof(LogProcessState)),
            "Agent {AgentId} reconciled ServerInstance {ServerInstanceId} as {ProcessState} with process {ProcessId}");
    private static readonly Action<ILogger, Guid, Guid, string, Exception?>
        LogInspectionSkipped = LoggerMessage.Define<Guid, Guid, string>(
            LogLevel.Warning,
            new EventId(501, nameof(LogInspectionSkipped)),
            "Agent {AgentId} could not reconcile ServerInstance {ServerInstanceId} because {Failure}");

    public async Task ReconcileAsync(
        AgentCredential credential,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credential);
        IReadOnlyList<AssignedAgentServerInstance> instances = await retry.ExecuteAsync(
            token => apiClient.ListServerInstancesAsync(credential, token),
            cancellationToken);
        metricsSampler.Retain(instances.Select(instance => instance.Id).ToHashSet());

        foreach (AssignedAgentServerInstance instance in instances)
        {
            ProcessSupervisorResolution resolution = supervisors.Resolve(
                instance.Id,
                new ProcessSupervisorRequest(
                    instance.Profile,
                    instance.ExecutablePath,
                    instance.Arguments,
                    instance.WorkingDirectory,
                    instance.ProcessName,
                    instance.DataDirectory,
                    instance.Identity));
            if (resolution.Supervisor is null)
            {
                LogInspectionSkipped(
                    logger,
                    credential.AgentId,
                    instance.Id,
                    resolution.Failure.ToString(),
                    null);
                continue;
            }

            ProcessSupervisorResult inspection = await resolution.Supervisor.InspectAsync(
                cancellationToken);
            AgentProcessStateReport? report = CreateReport(instance, inspection);
            if (report is null)
            {
                LogInspectionSkipped(
                    logger,
                    credential.AgentId,
                    instance.Id,
                    inspection.Failure.ToString(),
                    null);
                continue;
            }

            await retry.ExecuteAsync(
                token => apiClient.ReportServerInstanceStateAsync(
                    credential,
                    instance.Id,
                    report,
                    token),
                cancellationToken);
            LogProcessState(
                logger,
                credential.AgentId,
                instance.Id,
                report.Status.ToString(),
                report.Identity?.ProcessId,
                null);
        }
    }

    private AgentProcessStateReport? CreateReport(
        AssignedAgentServerInstance instance,
        ProcessSupervisorResult inspection) => inspection.Status switch
        {
            ProcessSupervisorStatus.Running when inspection.Identity is not null &&
                inspection.Snapshot is not null =>
                AgentProcessStateReport.Running(
                    inspection.Identity,
                    metricsSampler.Capture(instance.Id, inspection.Snapshot)),
            ProcessSupervisorStatus.NotRunning or ProcessSupervisorStatus.AlreadyStopped =>
                MissingProcessReport(instance.Id, instance.ReportedStatus),
            ProcessSupervisorStatus.StaleProcessId =>
                MissingProcessReport(instance.Id, instance.ReportedStatus),
            _ => null,
        };

    private AgentProcessStateReport MissingProcessReport(
        Guid serverInstanceId,
        AgentServerInstanceStatus previousStatus)
    {
        metricsSampler.Reset(serverInstanceId);
        return previousStatus switch
        {
            AgentServerInstanceStatus.Running or AgentServerInstanceStatus.Starting =>
                AgentProcessStateReport.Crashed(),
            AgentServerInstanceStatus.Crashed => AgentProcessStateReport.Crashed(),
            _ => AgentProcessStateReport.Stopped(),
        };
    }
}

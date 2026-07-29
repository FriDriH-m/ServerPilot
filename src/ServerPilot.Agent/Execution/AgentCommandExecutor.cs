using Microsoft.Extensions.Logging;
using ServerPilot.Agent.Api;
using ServerPilot.Agent.Credentials;
using ServerPilot.Agent.Looping;
using ServerPilot.Agent.Processes;

namespace ServerPilot.Agent.Execution;

public sealed class AgentCommandExecutor(
    IAgentApiClient apiClient,
    AgentRetryExecutor retry,
    IProcessSupervisorRegistry supervisors,
    ILogger<AgentCommandExecutor> logger) : IAgentCommandExecutor
{
    private const string ProcessOperationFailedMessage =
        "The local process operation did not reach the required state.";
    private const string InvalidConfigurationMessage =
        "The stored process configuration cannot be executed safely.";

    private static readonly Action<ILogger, Guid, Guid, Guid, Guid, string, Exception?>
        LogExecutionStarted = LoggerMessage.Define<Guid, Guid, Guid, Guid, string>(
            LogLevel.Information,
            new EventId(400, nameof(LogExecutionStarted)),
            "Agent {AgentId} is executing ServerCommand {CommandId} for ServerInstance {ServerInstanceId} with CorrelationId {CorrelationId} as {CommandType}");

    private static readonly Action<ILogger, Guid, Guid, Guid, Guid, Exception?>
        LogExecutionCompleted = LoggerMessage.Define<Guid, Guid, Guid, Guid>(
            LogLevel.Information,
            new EventId(401, nameof(LogExecutionCompleted)),
            "Agent {AgentId} completed ServerCommand {CommandId} for ServerInstance {ServerInstanceId} with CorrelationId {CorrelationId}");

    private static readonly Action<ILogger, Guid, Guid, Guid, Guid, string, Exception?>
        LogExecutionFailed = LoggerMessage.Define<Guid, Guid, Guid, Guid, string>(
            LogLevel.Warning,
            new EventId(402, nameof(LogExecutionFailed)),
            "Agent {AgentId} failed ServerCommand {CommandId} for ServerInstance {ServerInstanceId} with CorrelationId {CorrelationId} using ErrorCode {ErrorCode}");

    public async Task ExecuteAsync(
        AgentCredential credential,
        AgentCommandExecution execution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(execution);

        ClaimedAgentCommand command = execution.Command;
        if (!execution.RunningReported)
        {
            await retry.ExecuteAsync(
                token => apiClient.MarkCommandRunningAsync(credential, command, token),
                cancellationToken);
            execution.MarkRunningReported();
            LogExecutionStarted(
                logger,
                credential.AgentId,
                command.Id,
                command.ServerInstanceId,
                command.CorrelationId,
                command.Type.ToString(),
                null);
        }

        if (execution.Outcome is null)
        {
            AgentCommandOutcome outcome = await ExecuteProcessOnceAsync(
                command,
                cancellationToken);
            execution.RecordOutcome(outcome);
        }

        AgentCommandOutcome recordedOutcome = execution.Outcome!;
        if (recordedOutcome.ProcessState is not null && !execution.ProcessStateReported)
        {
            await retry.ExecuteAsync(
                token => apiClient.ReportServerInstanceStateAsync(
                    credential,
                    command.ServerInstanceId,
                    recordedOutcome.ProcessState,
                    token),
                cancellationToken);
            execution.MarkProcessStateReported();
        }

        if (recordedOutcome.Succeeded)
        {
            await retry.ExecuteAsync(
                token => apiClient.CompleteCommandAsync(credential, command, token),
                cancellationToken);
            LogExecutionCompleted(
                logger,
                credential.AgentId,
                command.Id,
                command.ServerInstanceId,
                command.CorrelationId,
                null);
            return;
        }

        await retry.ExecuteAsync(
            token => apiClient.FailCommandAsync(
                credential,
                command,
                recordedOutcome.ErrorCode!,
                recordedOutcome.ErrorMessage!,
                token),
            cancellationToken);
        LogExecutionFailed(
            logger,
            credential.AgentId,
            command.Id,
            command.ServerInstanceId,
            command.CorrelationId,
            recordedOutcome.ErrorCode!,
            null);
    }

    private async Task<AgentCommandOutcome> ExecuteProcessOnceAsync(
        ClaimedAgentCommand command,
        CancellationToken cancellationToken)
    {
        ProcessSupervisorResolution resolution = supervisors.Resolve(
            command.ServerInstanceId,
            new ProcessSupervisorRequest(
                command.ServerInstance.ExecutablePath,
                command.ServerInstance.Arguments,
                command.ServerInstance.WorkingDirectory,
                command.ServerInstance.ProcessName));
        if (resolution.Supervisor is null)
        {
            return resolution.Failure switch
            {
                ProcessSupervisorResolutionFailure.ConfigurationChanged =>
                    AgentCommandOutcome.Failed(
                        "ProcessConfigurationChanged",
                        "The process configuration changed while the Agent was managing it."),
                _ => AgentCommandOutcome.Failed(
                    "InvalidProcessConfiguration",
                    InvalidConfigurationMessage),
            };
        }

        return command.Type switch
        {
            AgentCommandType.StartServer => await ExecuteStartAsync(
                resolution.Supervisor,
                cancellationToken),
            AgentCommandType.StopServer => await ExecuteStopAsync(
                resolution.Supervisor,
                cancellationToken),
            _ => AgentCommandOutcome.Failed(
                "UnsupportedCommandType",
                "The command type is not supported by this Agent."),
        };
    }

    private static async Task<AgentCommandOutcome> ExecuteStartAsync(
        IProcessSupervisor supervisor,
        CancellationToken cancellationToken)
    {
        ProcessSupervisorResult start = await supervisor.StartAsync(cancellationToken);
        if (start.Status is not (
                ProcessSupervisorStatus.Started or
                ProcessSupervisorStatus.AlreadyRunning or
                ProcessSupervisorStatus.Running))
        {
            return ProcessFailure(start);
        }

        ProcessSupervisorResult inspection = await supervisor.InspectAsync(cancellationToken);
        return inspection is
        {
            Status: ProcessSupervisorStatus.Running,
            Identity: not null,
        }
            ? AgentCommandOutcome.Completed(
                AgentProcessStateReport.Running(inspection.Identity))
            : ProcessFailure(inspection);
    }

    private static async Task<AgentCommandOutcome> ExecuteStopAsync(
        IProcessSupervisor supervisor,
        CancellationToken cancellationToken)
    {
        ProcessSupervisorResult stop = await supervisor.StopAsync(cancellationToken);
        if (stop.Status is not (
                ProcessSupervisorStatus.Stopped or
                ProcessSupervisorStatus.AlreadyStopped))
        {
            return ProcessFailure(stop);
        }

        ProcessSupervisorResult inspection = await supervisor.InspectAsync(cancellationToken);
        return inspection.Status == ProcessSupervisorStatus.NotRunning
            ? AgentCommandOutcome.Completed(AgentProcessStateReport.Stopped())
            : ProcessFailure(inspection);
    }

    private static AgentCommandOutcome ProcessFailure(ProcessSupervisorResult result)
    {
        string errorCode = result.Status == ProcessSupervisorStatus.StaleProcessId
            ? "StaleProcessIdentity"
            : result.Failure == ProcessSupervisorFailure.None
                ? "ProcessStateVerificationFailed"
                : result.Failure.ToString();
        return AgentCommandOutcome.Failed(errorCode, ProcessOperationFailedMessage);
    }
}

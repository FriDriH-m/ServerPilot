using ServerPilot.Agent.Api;
using ServerPilot.Agent.Configuration;
using ServerPilot.Agent.Credentials;
using ServerPilot.Agent.Execution;

namespace ServerPilot.Agent.Looping;

public sealed class AgentLoopService(
    AgentOptions options,
    IAgentApiClient apiClient,
    AgentRetryExecutor retry,
    PeriodicAgentLoop periodicLoop,
    IAgentCommandExecutor commandExecutor,
    ILogger<AgentLoopService> logger)
{
    private AgentCommandExecution? activeCommand;

    private static readonly Action<ILogger, Guid, int, Exception?> LogTransientFailure =
        LoggerMessage.Define<Guid, int>(
            LogLevel.Warning,
            new EventId(200, nameof(LogTransientFailure)),
            "Agent {AgentId} will resume its loop after {Attempts} transient request attempts");

    private static readonly Action<ILogger, Guid, string, string, Exception?> LogFatalFailure =
        LoggerMessage.Define<Guid, string, string>(
            LogLevel.Error,
            new EventId(201, nameof(LogFatalFailure)),
            "Agent {AgentId} stopped its {LoopName} loop because of {FailureKind} API failure");

    private static readonly Action<ILogger, Guid, Guid, Guid, string, Exception?> LogCommandReserved =
        LoggerMessage.Define<Guid, Guid, Guid, string>(
            LogLevel.Information,
            new EventId(202, nameof(LogCommandReserved)),
            "Agent {AgentId} reserved ServerCommand {CommandId} with CorrelationId {CorrelationId} as {DeliveryKind}");

    public async Task RunAsync(AgentCredential credential, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credential);

        using CancellationTokenSource loopCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        TaskCompletionSource<AgentApiException> fatalFailure = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Task heartbeatLoop = periodicLoop.RunAsync(
            options.HeartbeatInterval,
            token => ExecuteHeartbeatAsync(credential, fatalFailure, token),
            loopCancellation.Token);
        Task pollingLoop = periodicLoop.RunAsync(
            options.CommandPollingInterval,
            token => ExecutePollingAsync(credential, fatalFailure, token),
            loopCancellation.Token);
        Task allLoops = Task.WhenAll(heartbeatLoop, pollingLoop);

        Task completed = await Task.WhenAny(allLoops, fatalFailure.Task);
        if (completed == fatalFailure.Task)
        {
            loopCancellation.Cancel();
            await IgnoreExpectedCancellationAsync(allLoops);
            throw new AgentLoopFatalException(await fatalFailure.Task);
        }

        try
        {
            await allLoops;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during graceful host shutdown.
        }
    }

    private async Task<bool> ExecuteHeartbeatAsync(
        AgentCredential credential,
        TaskCompletionSource<AgentApiException> fatalFailure,
        CancellationToken cancellationToken)
    {
        try
        {
            await retry.ExecuteAsync(
                token => apiClient.SendHeartbeatAsync(credential, token),
                cancellationToken);
            return true;
        }
        catch (AgentRetryExhaustedException exception)
        {
            LogTransientFailure(logger, credential.AgentId, exception.Attempts, exception);
            return true;
        }
        catch (AgentApiException exception) when (
            exception.FailureKind != AgentApiFailureKind.Transient)
        {
            LogFatalFailure(
                logger,
                credential.AgentId,
                "heartbeat",
                exception.FailureKind.ToString(),
                exception);
            fatalFailure.TrySetResult(exception);
            return false;
        }
    }

    private async Task<bool> ExecutePollingAsync(
        AgentCredential credential,
        TaskCompletionSource<AgentApiException> fatalFailure,
        CancellationToken cancellationToken)
    {
        try
        {
            AgentCommandExecution? execution = Volatile.Read(ref activeCommand);
            if (execution is null)
            {
                ClaimedAgentCommand? command = await retry.ExecuteAsync(
                    token => apiClient.ClaimNextCommandAsync(credential, token),
                    cancellationToken);
                if (command is null)
                {
                    return true;
                }

                AgentCommandExecution newExecution = new(command);
                execution = Interlocked.CompareExchange(
                    ref activeCommand,
                    newExecution,
                    null) ?? newExecution;
                if (ReferenceEquals(execution, newExecution))
                {
                    LogCommandReserved(
                        logger,
                        credential.AgentId,
                        command.Id,
                        command.CorrelationId,
                        command.DeliveryKind,
                        null);
                }
            }

            await commandExecutor.ExecuteAsync(credential, execution, cancellationToken);
            Interlocked.CompareExchange(ref activeCommand, null, execution);
            return true;
        }
        catch (AgentRetryExhaustedException exception)
        {
            LogTransientFailure(logger, credential.AgentId, exception.Attempts, exception);
            return true;
        }
        catch (AgentApiException exception) when (
            exception.FailureKind != AgentApiFailureKind.Transient)
        {
            LogFatalFailure(
                logger,
                credential.AgentId,
                "command polling",
                exception.FailureKind.ToString(),
                exception);
            fatalFailure.TrySetResult(exception);
            return false;
        }
    }

    private static async Task IgnoreExpectedCancellationAsync(Task allLoops)
    {
        try
        {
            await allLoops;
        }
        catch (OperationCanceledException)
        {
            // Expected when a fatal failure ends the paired loop.
        }
    }
}

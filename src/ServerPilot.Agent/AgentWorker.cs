using ServerPilot.Agent.Looping;
using ServerPilot.Agent.Runtime;

namespace ServerPilot.Agent;

public sealed class AgentWorker(
    AgentRuntime runtime,
    AgentLoopService loops,
    IHostApplicationLifetime applicationLifetime,
    ILogger<AgentWorker> logger) : BackgroundService
{
    private static readonly Action<ILogger, Guid, Exception?> LogAgentStarted =
        LoggerMessage.Define<Guid>(
            LogLevel.Information,
            new EventId(1, nameof(LogAgentStarted)),
            "ServerPilot Agent {AgentId} started");

    private static readonly Action<ILogger, Guid, Exception?> LogAgentStopped =
        LoggerMessage.Define<Guid>(
            LogLevel.Information,
            new EventId(2, nameof(LogAgentStopped)),
            "ServerPilot Agent {AgentId} stopped");

    private static readonly Action<ILogger, Guid, string, Exception?> LogFatalAgentLoopFailure =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Critical,
            new EventId(3, nameof(LogFatalAgentLoopFailure)),
            "ServerPilot Agent {AgentId} is stopping after a non-retryable {FailureKind} API failure");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Guid agentId = runtime.GetCredential().AgentId;
        LogAgentStarted(logger, agentId, null);

        try
        {
            await loops.RunAsync(runtime.GetCredential(), stoppingToken);
        }
        catch (AgentLoopFatalException exception)
        {
            LogFatalAgentLoopFailure(logger, agentId, exception.FailureKind.ToString(), exception);
            applicationLifetime.StopApplication();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected during graceful host shutdown.
        }
        finally
        {
            LogAgentStopped(logger, agentId, null);
        }
    }
}

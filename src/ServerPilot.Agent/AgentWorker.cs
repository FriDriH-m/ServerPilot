namespace ServerPilot.Agent;

public sealed class AgentWorker(ILogger<AgentWorker> logger) : BackgroundService
{
    private static readonly Action<ILogger, Exception?> LogAgentStarted = LoggerMessage.Define(
        LogLevel.Information,
        new EventId(1, nameof(LogAgentStarted)),
        "ServerPilot Agent started");

    private static readonly Action<ILogger, Exception?> LogAgentStopped = LoggerMessage.Define(
        LogLevel.Information,
        new EventId(2, nameof(LogAgentStopped)),
        "ServerPilot Agent stopped");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogAgentStarted(logger, null);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected during graceful host shutdown.
        }
        finally
        {
            LogAgentStopped(logger, null);
        }
    }
}

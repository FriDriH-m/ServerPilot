namespace ServerPilot.Api.Diagnostics;

internal sealed class ApiLifetimeLogger(
    ILogger<ApiLifetimeLogger> logger) : IHostedService
{
    private static readonly Action<ILogger, Exception?> LogApiStarted =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(1100, nameof(LogApiStarted)),
            "ServerPilot API started");
    private static readonly Action<ILogger, Exception?> LogApiStopping =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(1101, nameof(LogApiStopping)),
            "ServerPilot API stopping");

    public Task StartAsync(CancellationToken cancellationToken)
    {
        LogApiStarted(logger, null);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        LogApiStopping(logger, null);
        return Task.CompletedTask;
    }
}

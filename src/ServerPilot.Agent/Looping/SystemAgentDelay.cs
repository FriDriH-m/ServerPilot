namespace ServerPilot.Agent.Looping;

public sealed class SystemAgentDelay : IAgentDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}

namespace ServerPilot.Agent.Looping;

public interface IAgentDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

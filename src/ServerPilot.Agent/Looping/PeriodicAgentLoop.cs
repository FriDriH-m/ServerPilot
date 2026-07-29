namespace ServerPilot.Agent.Looping;

public sealed class PeriodicAgentLoop(IAgentDelay delay)
{
    public async Task RunAsync(
        TimeSpan interval,
        Func<CancellationToken, Task<bool>> iteration,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(iteration);

        while (!cancellationToken.IsCancellationRequested)
        {
            bool shouldContinue = await iteration(cancellationToken);
            if (!shouldContinue)
            {
                return;
            }

            await delay.DelayAsync(interval, cancellationToken);
        }
    }
}

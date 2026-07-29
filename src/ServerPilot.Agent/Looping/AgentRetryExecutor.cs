using System.Net.Http;
using ServerPilot.Agent.Api;

namespace ServerPilot.Agent.Looping;

public sealed class AgentRetryExecutor(IAgentDelay delay)
{
    private const int MaximumAttempts = 4;
    private const int InitialDelayMilliseconds = 1_000;
    private const int MaximumDelayMilliseconds = 30_000;

    public async Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await ExecuteAsync(
            async token =>
            {
                await operation(token);
                return true;
            },
            cancellationToken);
    }

    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        for (int attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await operation(cancellationToken);
            }
            catch (AgentApiException exception) when (
                exception.FailureKind == AgentApiFailureKind.Transient)
            {
                await RetryAfterTransientFailureAsync(attempt, exception, cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                await RetryAfterTransientFailureAsync(attempt, exception, cancellationToken);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                await RetryAfterTransientFailureAsync(attempt, exception, cancellationToken);
            }
        }
    }

    private async Task RetryAfterTransientFailureAsync(
        int attempt,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (attempt >= MaximumAttempts)
        {
            throw new AgentRetryExhaustedException(attempt, exception);
        }

        await delay.DelayAsync(GetRetryDelay(attempt), cancellationToken);
    }

    private static TimeSpan GetRetryDelay(int attempt)
    {
        int exponentialDelay = InitialDelayMilliseconds * (1 << (attempt - 1));
        int cappedDelay = Math.Min(exponentialDelay, MaximumDelayMilliseconds);
        double jitterMultiplier = 0.75 + (Random.Shared.NextDouble() * 0.5);
        return TimeSpan.FromMilliseconds(cappedDelay * jitterMultiplier);
    }
}

public sealed class AgentRetryExhaustedException(int attempts, Exception innerException)
    : Exception($"Agent operation failed after {attempts} transient attempts.", innerException)
{
    public int Attempts { get; } = attempts;
}

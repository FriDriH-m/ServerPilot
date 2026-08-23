namespace ServerPilot.Agent.Processes;

public sealed record ProcessMetricSample(
    double? CpuUsagePercent,
    long WorkingSetBytes,
    long UptimeSeconds);

public sealed class ProcessMetricsSampler
{
    private readonly Dictionary<Guid, PreviousSample> previousSamples = [];
    private readonly TimeProvider timeProvider;
    private readonly int processorCount;

    public ProcessMetricsSampler(TimeProvider timeProvider)
        : this(timeProvider, Environment.ProcessorCount)
    {
    }

    internal ProcessMetricsSampler(TimeProvider timeProvider, int processorCount)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfLessThan(processorCount, 1);

        this.timeProvider = timeProvider;
        this.processorCount = processorCount;
    }

    public ProcessMetricSample? Capture(Guid serverInstanceId, ProcessSnapshot snapshot)
    {
        if (serverInstanceId == Guid.Empty)
        {
            throw new ArgumentException(
                "Server instance ID cannot be empty.",
                nameof(serverInstanceId));
        }

        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.TotalProcessorTime is not TimeSpan totalProcessorTime ||
            snapshot.WorkingSetBytes is not long workingSetBytes)
        {
            return null;
        }

        DateTimeOffset observedAt = timeProvider.GetUtcNow().ToUniversalTime();
        long observedTimestamp = timeProvider.GetTimestamp();
        double? cpuUsagePercent = null;

        if (previousSamples.TryGetValue(serverInstanceId, out PreviousSample? previous) &&
            previous.ProcessId == snapshot.ProcessId &&
            previous.ProcessStartedAt == snapshot.StartedAtUtc.ToUniversalTime() &&
            totalProcessorTime >= previous.TotalProcessorTime)
        {
            double elapsedProcessorMilliseconds =
                (totalProcessorTime - previous.TotalProcessorTime).TotalMilliseconds;
            double elapsedWallMilliseconds = timeProvider
                .GetElapsedTime(previous.ObservedTimestamp, observedTimestamp)
                .TotalMilliseconds;
            if (elapsedWallMilliseconds > 0)
            {
                cpuUsagePercent = Math.Clamp(
                    elapsedProcessorMilliseconds / elapsedWallMilliseconds / processorCount * 100d,
                    0d,
                    100d);
            }
        }

        previousSamples[serverInstanceId] = new PreviousSample(
            snapshot.ProcessId,
            snapshot.StartedAtUtc.ToUniversalTime(),
            totalProcessorTime,
            observedTimestamp);

        long uptimeSeconds = Math.Max(
            0,
            (long)Math.Floor((observedAt - snapshot.StartedAtUtc.ToUniversalTime()).TotalSeconds));
        return new ProcessMetricSample(
            cpuUsagePercent,
            Math.Max(0, workingSetBytes),
            uptimeSeconds);
    }

    public void Reset(Guid serverInstanceId) => previousSamples.Remove(serverInstanceId);

    public void Retain(IReadOnlySet<Guid> serverInstanceIds)
    {
        ArgumentNullException.ThrowIfNull(serverInstanceIds);
        foreach (Guid serverInstanceId in previousSamples.Keys
                     .Where(id => !serverInstanceIds.Contains(id))
                     .ToArray())
        {
            previousSamples.Remove(serverInstanceId);
        }
    }

    private sealed record PreviousSample(
        int ProcessId,
        DateTimeOffset ProcessStartedAt,
        TimeSpan TotalProcessorTime,
        long ObservedTimestamp);
}

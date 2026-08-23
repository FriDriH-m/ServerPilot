using ServerPilot.Agent.Processes;

namespace ServerPilot.UnitTests.AgentProcesses;

public sealed class ProcessMetricsSamplerTests
{
    private static readonly DateTimeOffset StartedAt =
        new(2026, 8, 23, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CalculatesCpuFromTwoSamplesAndBoundsMemoryAndUptime()
    {
        MutableTimeProvider timeProvider = new(StartedAt.AddHours(1));
        ProcessMetricsSampler sampler = new(timeProvider, processorCount: 4);
        Guid serverInstanceId = Guid.NewGuid();

        ProcessMetricSample first = Assert.IsType<ProcessMetricSample>(sampler.Capture(
            serverInstanceId,
            CreateSnapshot(TimeSpan.FromSeconds(10), 256 * 1024 * 1024)));
        timeProvider.Advance(TimeSpan.FromSeconds(10));
        ProcessMetricSample second = Assert.IsType<ProcessMetricSample>(sampler.Capture(
            serverInstanceId,
            CreateSnapshot(TimeSpan.FromSeconds(12), 300 * 1024 * 1024)));

        Assert.Null(first.CpuUsagePercent);
        Assert.Equal(3_600, first.UptimeSeconds);
        Assert.Equal(256 * 1024 * 1024, first.WorkingSetBytes);
        Assert.Equal(5d, second.CpuUsagePercent!.Value, precision: 6);
        Assert.Equal(3_610, second.UptimeSeconds);
    }

    [Fact]
    public void NewProcessIdentityStartsANewCpuBaseline()
    {
        MutableTimeProvider timeProvider = new(StartedAt.AddMinutes(1));
        ProcessMetricsSampler sampler = new(timeProvider, processorCount: 1);
        Guid serverInstanceId = Guid.NewGuid();
        sampler.Capture(serverInstanceId, CreateSnapshot(TimeSpan.FromSeconds(1), 1));
        timeProvider.Advance(TimeSpan.FromSeconds(1));

        ProcessMetricSample restarted = Assert.IsType<ProcessMetricSample>(sampler.Capture(
            serverInstanceId,
            CreateSnapshot(TimeSpan.FromMilliseconds(500), 2) with
            {
                ProcessId = 43,
                StartedAtUtc = StartedAt.AddSeconds(30),
            }));

        Assert.Null(restarted.CpuUsagePercent);
    }

    [Fact]
    public void UnavailableCountersDoNotProduceAMetricSample()
    {
        ProcessMetricsSampler sampler = new(new MutableTimeProvider(StartedAt), processorCount: 1);

        ProcessMetricSample? sample = sampler.Capture(
            Guid.NewGuid(),
            CreateSnapshot(TimeSpan.Zero, 0) with
            {
                TotalProcessorTime = null,
                WorkingSetBytes = null,
            });

        Assert.Null(sample);
    }

    private static ProcessSnapshot CreateSnapshot(TimeSpan totalProcessorTime, long workingSet) =>
        new(
            42,
            StartedAt,
            @"C:\Servers\server.exe",
            "server",
            totalProcessorTime,
            workingSet);

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private long timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() => utcNow;

        public override long GetTimestamp() => timestamp;

        public void Advance(TimeSpan duration)
        {
            utcNow += duration;
            timestamp += duration.Ticks;
        }
    }
}

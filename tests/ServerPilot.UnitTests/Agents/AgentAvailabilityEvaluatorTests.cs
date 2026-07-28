using ServerPilot.Application.Agents;

namespace ServerPilot.UnitTests.Agents;

public sealed class AgentAvailabilityEvaluatorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 28, 21, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Threshold = TimeSpan.FromSeconds(30);

    [Fact]
    public void AgentWithoutHeartbeatIsOffline()
    {
        AgentAvailabilityStatus status = AgentAvailabilityEvaluator.Evaluate(
            null,
            Now,
            Threshold);

        Assert.Equal(AgentAvailabilityStatus.Offline, status);
    }

    [Fact]
    public void HeartbeatExactlyAtThresholdIsOnline()
    {
        AgentAvailabilityStatus status = AgentAvailabilityEvaluator.Evaluate(
            Now - Threshold,
            Now,
            Threshold);

        Assert.Equal(AgentAvailabilityStatus.Online, status);
    }

    [Fact]
    public void HeartbeatOlderThanThresholdIsOffline()
    {
        AgentAvailabilityStatus status = AgentAvailabilityEvaluator.Evaluate(
            Now - Threshold - TimeSpan.FromTicks(1),
            Now,
            Threshold);

        Assert.Equal(AgentAvailabilityStatus.Offline, status);
    }

    [Fact]
    public void RejectsNonPositiveThreshold()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AgentAvailabilityEvaluator.Evaluate(Now, Now, TimeSpan.Zero));
    }
}

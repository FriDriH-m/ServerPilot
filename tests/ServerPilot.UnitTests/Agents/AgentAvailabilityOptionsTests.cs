using ServerPilot.Application.Agents;

namespace ServerPilot.UnitTests.Agents;

public sealed class AgentAvailabilityOptionsTests
{
    [Fact]
    public void DefaultsToThirtySecondThreshold()
    {
        AgentAvailabilityOptions options = new();

        options.Validate();

        Assert.Equal(30, options.OfflineThresholdSeconds);
        Assert.Equal(TimeSpan.FromSeconds(30), options.OfflineThreshold);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(86_401)]
    public void RejectsThresholdOutsideAllowedRange(int thresholdSeconds)
    {
        AgentAvailabilityOptions options = new()
        {
            OfflineThresholdSeconds = thresholdSeconds,
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }
}

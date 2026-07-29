using ServerPilot.Agent.Configuration;

namespace ServerPilot.UnitTests.AgentBootstrap;

public sealed class AgentOptionsTests
{
    [Fact]
    public void AcceptsLoopbackHttpForLocalDevelopment()
    {
        AgentOptions options = CreateValidOptions("http://localhost:5050");

        options.Validate();

        Assert.Equal(new Uri("http://localhost:5050/"), options.GetApiBaseUri());
        Assert.Equal(TimeSpan.FromSeconds(10), options.HeartbeatInterval);
        Assert.Equal(TimeSpan.FromSeconds(5), options.CommandPollingInterval);
    }

    [Theory]
    [InlineData("http://api.example.test")]
    [InlineData("ftp://localhost")]
    [InlineData("https://api.example.test/?unexpected=true")]
    public void RejectsUnsafeOrInvalidApiUrl(string apiBaseUrl)
    {
        AgentOptions options = CreateValidOptions(apiBaseUrl);

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(86_401)]
    public void RejectsIntervalOutsideAllowedRange(int intervalSeconds)
    {
        AgentOptions options = new()
        {
            ApiBaseUrl = "https://api.example.test",
            Name = "test-agent",
            HeartbeatIntervalSeconds = intervalSeconds,
            CommandPollingIntervalSeconds = 5,
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    private static AgentOptions CreateValidOptions(string apiBaseUrl = "https://api.example.test") => new()
    {
        ApiBaseUrl = apiBaseUrl,
        Name = "test-agent",
        HeartbeatIntervalSeconds = 10,
        CommandPollingIntervalSeconds = 5,
    };
}

using ServerPilot.Application.Agents;

namespace ServerPilot.UnitTests.Agents;

public sealed class AgentCredentialFormatTests
{
    [Fact]
    public void AcceptsGeneratedCredentialShape()
    {
        string credential = AgentCredentialFormat.Prefix +
            new string('A', AgentCredentialFormat.RandomHexLength);

        Assert.True(AgentCredentialFormat.IsValid(credential));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer value")]
    [InlineData("spac_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("spac_GGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGG")]
    public void RejectsMalformedCredential(string? credential)
    {
        Assert.False(AgentCredentialFormat.IsValid(credential));
    }
}

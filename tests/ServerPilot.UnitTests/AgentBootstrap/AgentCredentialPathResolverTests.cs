using ServerPilot.Agent.Credentials;

namespace ServerPilot.UnitTests.AgentBootstrap;

public sealed class AgentCredentialPathResolverTests
{
    [Fact]
    public void ConsoleModeUsesCurrentUserLocalApplicationData()
    {
        string path = AgentCredentialPathResolver.GetCredentialPath(
            isWindowsService: false,
            Path.Combine("test", "local-app-data"));

        Assert.Equal(
            Path.Combine("test", "local-app-data", "ServerPilot", "agent-credential.dat"),
            path);
    }

    [Fact]
    public void WindowsServiceModeUsesCommonApplicationData()
    {
        string path = AgentCredentialPathResolver.GetCredentialPath(
            isWindowsService: true,
            Path.Combine("test", "program-data"));

        Assert.Equal(
            Path.Combine(
                "test",
                "program-data",
                "ServerPilot",
                "Agent",
                "agent-credential.dat"),
            path);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingApplicationDataDirectoryIsRejected(bool isWindowsService)
    {
        Assert.Throws<InvalidOperationException>(() =>
            AgentCredentialPathResolver.GetCredentialPath(isWindowsService, " "));
    }
}

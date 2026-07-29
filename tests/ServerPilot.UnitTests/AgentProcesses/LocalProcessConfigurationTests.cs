using ServerPilot.Agent.Processes;

namespace ServerPilot.UnitTests.AgentProcesses;

public sealed class LocalProcessConfigurationTests
{
    [Fact]
    public void CreatesConfigurationForSafeNativeExecutable()
    {
        LocalProcessConfigurationResult result = LocalProcessConfiguration.Create(
            @"C:\Servers\Example\server.exe",
            "--port 27015",
            @"C:\Servers\Example",
            "server");

        Assert.True(result.IsValid);
        Assert.NotNull(result.Configuration);
        Assert.Equal("server", result.Configuration.ProcessName);
    }

    [Theory]
    [InlineData(@"server.exe", LocalProcessConfigurationError.InvalidExecutablePath)]
    [InlineData(@"\\?\C:\Servers\server.exe", LocalProcessConfigurationError.InvalidExecutablePath)]
    [InlineData(@"C:\Servers\..\server.exe", LocalProcessConfigurationError.InvalidExecutablePath)]
    [InlineData(@"C:\Servers\start-server.bat", LocalProcessConfigurationError.UnsupportedExecutableType)]
    [InlineData(@"C:\Servers\other.exe", LocalProcessConfigurationError.ProcessNameMismatch)]
    public void RejectsUnsafeOrAmbiguousExecutable(
        string executablePath,
        LocalProcessConfigurationError expectedError)
    {
        LocalProcessConfigurationResult result = LocalProcessConfiguration.Create(
            executablePath,
            string.Empty,
            @"C:\Servers",
            "server");

        Assert.False(result.IsValid);
        Assert.Null(result.Configuration);
        Assert.Equal(expectedError, result.Error);
    }
}

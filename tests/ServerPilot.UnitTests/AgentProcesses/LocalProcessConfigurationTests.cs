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
    [InlineData(@"//?/C:/Servers/server.exe", LocalProcessConfigurationError.InvalidExecutablePath)]
    [InlineData(@"\??\C:\Servers\server.exe", LocalProcessConfigurationError.InvalidExecutablePath)]
    [InlineData(@"\\??\C:\Servers\server.exe", LocalProcessConfigurationError.InvalidExecutablePath)]
    [InlineData(@"\\server", LocalProcessConfigurationError.InvalidExecutablePath)]
    [InlineData(@"C:\Servers\..\server.exe", LocalProcessConfigurationError.InvalidExecutablePath)]
    [InlineData("C:\\Servers\\server.exe\u0000", LocalProcessConfigurationError.InvalidExecutablePath)]
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

    [Fact]
    public void AcceptsBackendMaximumPathLength()
    {
        string executablePath = CreatePathWithLength(2_048, "server.exe");
        string workingDirectory = CreatePathWithLength(2_048, "working");

        LocalProcessConfigurationResult result = LocalProcessConfiguration.Create(
            executablePath,
            string.Empty,
            workingDirectory,
            "server");

        Assert.True(result.IsValid);
        Assert.Equal(executablePath, result.Configuration!.ExecutablePath);
        Assert.Equal(workingDirectory, result.Configuration.WorkingDirectory);
    }

    [Fact]
    public void RejectsPathsBeyondBackendMaximumPathLength()
    {
        LocalProcessConfigurationResult result = LocalProcessConfiguration.Create(
            CreatePathWithLength(2_049, "server.exe"),
            string.Empty,
            @"C:\Servers",
            "server");

        Assert.False(result.IsValid);
        Assert.Equal(LocalProcessConfigurationError.InvalidExecutablePath, result.Error);
    }

    [Fact]
    public void AcceptsUncPathOnlyWhenServerAndShareArePresent()
    {
        LocalProcessConfigurationResult result = LocalProcessConfiguration.Create(
            @"\\server\share\server.exe",
            string.Empty,
            @"\\server\share",
            "server");

        Assert.True(result.IsValid);
    }

    private static string CreatePathWithLength(int length, string finalSegment)
    {
        const string prefix = @"C:\";
        string suffix = $@"\{finalSegment}";
        return prefix + new string('a', length - prefix.Length - suffix.Length) + suffix;
    }
}

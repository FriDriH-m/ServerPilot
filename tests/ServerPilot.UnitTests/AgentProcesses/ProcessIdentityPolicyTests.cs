using ServerPilot.Agent.Processes;

namespace ServerPilot.UnitTests.AgentProcesses;

public sealed class ProcessIdentityPolicyTests
{
    private static readonly DateTimeOffset StartedAt = new(
        2026,
        7,
        29,
        12,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public void MatchesEquivalentWindowsIdentity()
    {
        ProcessIdentity expected = new(42, StartedAt, @"C:\Servers\server.exe", "server.exe");
        ProcessSnapshot actual = new(42, StartedAt, @"c:/servers/server.exe", "SERVER");

        Assert.True(ProcessIdentityPolicy.Matches(expected, actual));
    }

    [Theory]
    [InlineData(43, 0, @"C:\Servers\server.exe", "server")]
    [InlineData(42, 1, @"C:\Servers\server.exe", "server")]
    [InlineData(42, 0, @"C:\Servers\other.exe", "server")]
    [InlineData(42, 0, @"C:\Servers\server.exe", "other")]
    public void RejectsStaleOrDifferentProcess(
        int processId,
        int additionalSeconds,
        string executablePath,
        string processName)
    {
        ProcessIdentity expected = new(42, StartedAt, @"C:\Servers\server.exe", "server");
        ProcessSnapshot actual = new(
            processId,
            StartedAt.AddSeconds(additionalSeconds),
            executablePath,
            processName);

        Assert.False(ProcessIdentityPolicy.Matches(expected, actual));
    }
}

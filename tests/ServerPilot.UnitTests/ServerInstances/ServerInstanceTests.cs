using ServerPilot.Domain.ServerInstances;

namespace ServerPilot.UnitTests.ServerInstances;

public sealed class ServerInstanceTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ConfigurationTrimsValuesAndRequiresAbsoluteWindowsPaths()
    {
        bool created = ServerInstanceConfiguration.TryCreate(
            "  Project Zomboid  ",
            "  C:\\Servers\\zomboid\\start-server.bat  ",
            "  -servername test  ",
            "  \\\\host\\servers\\zomboid  ",
            "  ProjectZomboid64.exe  ",
            out ServerInstanceConfiguration? configuration);

        Assert.True(created);
        ServerInstanceConfiguration value = Assert.IsType<ServerInstanceConfiguration>(configuration);
        Assert.Equal("Project Zomboid", value.Name);
        Assert.Equal("C:\\Servers\\zomboid\\start-server.bat", value.ExecutablePath);
        Assert.Equal("-servername test", value.Arguments);
        Assert.Equal("\\\\host\\servers\\zomboid", value.WorkingDirectory);
        Assert.Equal("ProjectZomboid64.exe", value.ProcessName);
    }

    [Theory]
    [InlineData("server.exe", "C:\\Servers", "server.exe")]
    [InlineData("C:\\Servers\\server.exe", "servers", "server.exe")]
    [InlineData("C:\\Servers\\server.exe", "C:\\Servers", "C:\\Servers\\server.exe")]
    [InlineData("\\\\?\\C:\\Servers\\server.exe", "C:\\Servers", "server.exe")]
    [InlineData("//?/C:/Servers/server.exe", "C:\\Servers", "server.exe")]
    [InlineData("//./C:/Servers/server.exe", "C:\\Servers", "server.exe")]
    [InlineData("\\/?/C:\\Servers/server.exe", "C:\\Servers", "server.exe")]
    [InlineData("C:\\Servers\\server.exe", "//?/C:/Servers", "server.exe")]
    [InlineData("\\\\\\share\\server.exe", "C:\\Servers", "server.exe")]
    [InlineData("\\\\server\\\\server.exe", "C:\\Servers", "server.exe")]
    [InlineData("C:\\Servers\\..\\server.exe", "C:\\Servers", "server.exe")]
    public void ConfigurationRejectsUnsafeOrRelativePaths(
        string executablePath,
        string workingDirectory,
        string processName)
    {
        bool created = ServerInstanceConfiguration.TryCreate(
            "Server",
            executablePath,
            string.Empty,
            workingDirectory,
            processName,
            out ServerInstanceConfiguration? configuration);

        Assert.False(created);
        Assert.Null(configuration);
    }

    [Theory]
    [InlineData("C:/Servers/server.exe", "C:/Servers")]
    [InlineData("\\\\server\\share\\server.exe", "\\\\server\\share")]
    [InlineData("//server/share/server.exe", "//server/share")]
    public void ConfigurationAcceptsValidDriveAndUncPaths(
        string executablePath,
        string workingDirectory)
    {
        bool created = ServerInstanceConfiguration.TryCreate(
            "Server",
            executablePath,
            string.Empty,
            workingDirectory,
            "server.exe",
            out ServerInstanceConfiguration? configuration);

        Assert.True(created);
        Assert.NotNull(configuration);
    }

    [Fact]
    public void CreateAndUpdatePreserveStateInvariants()
    {
        ServerInstance instance = ServerInstance.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            CreateConfiguration("First"),
            CreatedAt);
        DateTimeOffset updatedAt = CreatedAt.AddMinutes(1);

        instance.UpdateConfiguration(CreateConfiguration("Updated"), updatedAt);
        instance.RecordProcessState(
            ServerInstanceStatus.Running,
            42,
            CreatedAt.AddSeconds(30),
            updatedAt.AddSeconds(1));

        Assert.Equal("Updated", instance.Name);
        Assert.Equal(ServerInstanceStatus.Running, instance.Status);
        Assert.Equal(42, instance.LastProcessId);
        Assert.True(instance.IsActive);
        Assert.Equal(updatedAt.AddSeconds(1), instance.UpdatedAt);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            instance.UpdateConfiguration(CreateConfiguration("Stale"), CreatedAt));
        Assert.Equal(
            ServerInstanceStateReportResult.InvalidProcessIdentity,
            instance.RecordProcessState(
                ServerInstanceStatus.Stopped,
                0,
                null,
                updatedAt.AddSeconds(2)));
    }

    [Fact]
    public void StoppedInstanceIsNotActiveAndCanClearTrackedProcessId()
    {
        ServerInstance instance = ServerInstance.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            CreateConfiguration("Server"),
            CreatedAt);

        instance.RecordProcessState(
            ServerInstanceStatus.Running,
            42,
            CreatedAt.AddSeconds(30),
            CreatedAt.AddMinutes(1));
        instance.RecordProcessState(
            ServerInstanceStatus.Stopped,
            null,
            null,
            CreatedAt.AddMinutes(2));

        Assert.False(instance.IsActive);
        Assert.Null(instance.LastProcessId);
        Assert.Equal(ServerInstanceStatus.Stopped, instance.Status);
    }

    [Fact]
    public void ProcessStateReportsEnforceIdentityAndTransitions()
    {
        ServerInstance instance = ServerInstance.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            CreateConfiguration("Server"),
            CreatedAt);

        ServerInstanceStateReportResult invalidCrash = instance.RecordProcessState(
            ServerInstanceStatus.Crashed,
            null,
            null,
            CreatedAt.AddSeconds(1));
        ServerInstanceStateReportResult invalidRunning = instance.RecordProcessState(
            ServerInstanceStatus.Running,
            42,
            null,
            CreatedAt.AddSeconds(2));
        ServerInstanceStateReportResult stopped = instance.RecordProcessState(
            ServerInstanceStatus.Stopped,
            null,
            null,
            CreatedAt.AddSeconds(3));

        Assert.Equal(ServerInstanceStateReportResult.InvalidState, invalidCrash);
        Assert.Equal(ServerInstanceStateReportResult.InvalidProcessIdentity, invalidRunning);
        Assert.Equal(ServerInstanceStateReportResult.Succeeded, stopped);
        Assert.Equal(CreatedAt.AddSeconds(3), instance.LastStatusReportedAt);
    }

    [Fact]
    public void UnexpectedExitCanTransitionRunningToCrashedAndClearIdentity()
    {
        ServerInstance instance = ServerInstance.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            CreateConfiguration("Server"),
            CreatedAt);
        DateTimeOffset processStartedAt = CreatedAt.AddSeconds(1);

        instance.RecordProcessState(
            ServerInstanceStatus.Running,
            42,
            processStartedAt,
            CreatedAt.AddSeconds(2));
        ServerInstanceStateReportResult result = instance.RecordProcessState(
            ServerInstanceStatus.Crashed,
            null,
            null,
            CreatedAt.AddSeconds(3));

        Assert.Equal(ServerInstanceStateReportResult.Succeeded, result);
        Assert.Equal(ServerInstanceStatus.Crashed, instance.Status);
        Assert.Null(instance.LastProcessId);
        Assert.Null(instance.LastProcessStartedAt);
    }

    [Fact]
    public void ProcessStateReportWithSameTimestampMustBeAnExactRetry()
    {
        ServerInstance instance = ServerInstance.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            CreateConfiguration("Server"),
            CreatedAt);
        DateTimeOffset reportedAt = CreatedAt.AddSeconds(2);

        ServerInstanceStateReportResult applied = instance.RecordProcessState(
            ServerInstanceStatus.Running,
            42,
            CreatedAt.AddSeconds(1),
            reportedAt);
        ServerInstanceStateReportResult exactRetry = instance.RecordProcessState(
            ServerInstanceStatus.Running,
            42,
            CreatedAt.AddSeconds(1),
            reportedAt);
        ServerInstanceStateReportResult conflictingRetry = instance.RecordProcessState(
            ServerInstanceStatus.Crashed,
            null,
            null,
            reportedAt);

        Assert.Equal(ServerInstanceStateReportResult.Succeeded, applied);
        Assert.Equal(ServerInstanceStateReportResult.AlreadyApplied, exactRetry);
        Assert.Equal(ServerInstanceStateReportResult.StaleReport, conflictingRetry);
        Assert.Equal(ServerInstanceStatus.Running, instance.Status);
        Assert.Equal(42, instance.LastProcessId);
    }

    private static ServerInstanceConfiguration CreateConfiguration(string name)
    {
        bool created = ServerInstanceConfiguration.TryCreate(
            name,
            "C:\\Servers\\server.exe",
            string.Empty,
            "C:\\Servers",
            "server.exe",
            out ServerInstanceConfiguration? configuration);

        Assert.True(created);
        return Assert.IsType<ServerInstanceConfiguration>(configuration);
    }
}

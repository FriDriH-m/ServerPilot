using System.Text;
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
    public void ProjectZomboidConfigurationAppliesCanonicalDefaultsAndPaths()
    {
        bool created = ServerInstanceConfiguration.TryCreate(
            ServerInstanceProfile.ProjectZomboid,
            "Zomboid",
            @"C:\Servers\PZ\StartServer64.bat",
            string.Empty,
            @"C:\Servers\PZ",
            "java",
            @"C:\Servers\PZ Data",
            out ServerInstanceConfiguration? configuration);

        Assert.True(created);
        Assert.Equal(ServerInstanceProfile.ProjectZomboid, configuration!.Profile);
        Assert.Equal(@"C:\Servers\PZ Data", configuration.DataDirectory);

        ProjectZomboidServerPaths paths = ProjectZomboidServerPaths.Create(
            configuration.DataDirectory!);
        Assert.Equal(@"C:\Servers\PZ Data\Server\servertest.ini", paths.MainConfigurationPath);
        Assert.Equal(@"C:\Servers\PZ Data\Logs", paths.LogsDirectory);
        Assert.Equal(
            @"C:\Servers\PZ Data\Saves\Multiplayer\servertest",
            paths.SaveDirectory);
    }

    [Theory]
    [InlineData(@"C:\Servers\PZ\other.bat", "", @"C:\Servers\PZ", "java", @"C:\Data")]
    [InlineData(@"C:\Servers\PZ\StartServer64.bat", "-x", @"C:\Servers\PZ", "java", @"C:\Data")]
    [InlineData(@"C:\Servers\PZ\StartServer64.bat", "", @"C:\Servers\Other", "java", @"C:\Data")]
    [InlineData(@"C:\Servers\PZ\StartServer64.bat", "", @"C:\Servers\PZ", "javaw", @"C:\Data")]
    [InlineData(@"C:\Servers\PZ\StartServer64.bat", "", @"C:\Servers\PZ", "java", @"C:\Data&whoami")]
    public void ProjectZomboidConfigurationRejectsAmbiguousLaunchInputs(
        string executablePath,
        string arguments,
        string workingDirectory,
        string processName,
        string dataDirectory)
    {
        bool created = ServerInstanceConfiguration.TryCreate(
            ServerInstanceProfile.ProjectZomboid,
            "Zomboid",
            executablePath,
            arguments,
            workingDirectory,
            processName,
            dataDirectory,
            out ServerInstanceConfiguration? configuration);

        Assert.False(created);
        Assert.Null(configuration);
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

    [Fact]
    public void RunningMetricsAreValidatedPersistedRetriedAndCleared()
    {
        ServerInstance instance = ServerInstance.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            CreateConfiguration("Server"),
            CreatedAt);
        DateTimeOffset reportedAt = CreatedAt.AddMinutes(1);
        DateTimeOffset processStartedAt = CreatedAt.AddSeconds(1).AddTicks(7);
        ServerInstanceMetricReport metrics = new(25.5, 268_435_456, 3_600);

        ServerInstanceStateReportResult invalid = instance.RecordProcessState(
            ServerInstanceStatus.Running,
            42,
            processStartedAt,
            reportedAt,
            new ServerInstanceMetricReport(double.NaN, -1, -1));
        ServerInstanceStateReportResult applied = instance.RecordProcessState(
            ServerInstanceStatus.Running,
            42,
            processStartedAt,
            reportedAt,
            metrics);
        ServerInstanceStateReportResult retry = instance.RecordProcessState(
            ServerInstanceStatus.Running,
            42,
            processStartedAt,
            reportedAt,
            metrics);

        Assert.Equal(ServerInstanceStateReportResult.InvalidMetrics, invalid);
        Assert.Equal(ServerInstanceStateReportResult.Succeeded, applied);
        Assert.Equal(ServerInstanceStateReportResult.AlreadyApplied, retry);
        Assert.Equal(25.5, instance.LastCpuUsagePercent);
        Assert.Equal(268_435_456, instance.LastWorkingSetBytes);
        Assert.Equal(3_600, instance.LastUptimeSeconds);
        Assert.Equal(reportedAt, instance.LastMetricsReportedAt);

        ServerInstanceStateReportResult stateOnlyReport = instance.RecordProcessState(
            ServerInstanceStatus.Running,
            42,
            processStartedAt,
            reportedAt.AddMilliseconds(500));

        Assert.Equal(ServerInstanceStateReportResult.Succeeded, stateOnlyReport);
        Assert.Equal(25.5, instance.LastCpuUsagePercent);
        Assert.Equal(reportedAt, instance.LastMetricsReportedAt);

        instance.RecordProcessState(
            ServerInstanceStatus.Stopped,
            null,
            null,
            reportedAt.AddSeconds(1));

        Assert.Null(instance.LastCpuUsagePercent);
        Assert.Null(instance.LastWorkingSetBytes);
        Assert.Null(instance.LastUptimeSeconds);
        Assert.Null(instance.LastMetricsReportedAt);
    }

    [Fact]
    public void ProjectZomboidLogsAppendIdempotentlyAndResetAfterRotation()
    {
        ServerInstance instance = ServerInstance.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            CreateProjectZomboidConfiguration(@"C:\Data\PZ"),
            CreatedAt);
        string source = ServerLogSourceIdentifier.Create(
            instance.Profile,
            instance.DataDirectory)!;
        Guid stream = Guid.NewGuid();

        ServerInstanceStateReportResult initial = instance.RecordProcessState(
            ServerInstanceStatus.Stopped,
            null,
            null,
            CreatedAt.AddSeconds(1),
            logs: new ServerInstanceLogReport(
                ServerInstanceLogStatus.Available,
                source,
                stream,
                0,
                6,
                true,
                "first\n"));
        ServerInstanceStateReportResult append = instance.RecordProcessState(
            ServerInstanceStatus.Stopped,
            null,
            null,
            CreatedAt.AddSeconds(2),
            logs: new ServerInstanceLogReport(
                ServerInstanceLogStatus.Available,
                source,
                stream,
                6,
                13,
                false,
                "second\n"));
        ServerInstanceStateReportResult retry = instance.RecordProcessState(
            ServerInstanceStatus.Stopped,
            null,
            null,
            CreatedAt.AddSeconds(3),
            logs: new ServerInstanceLogReport(
                ServerInstanceLogStatus.Available,
                source,
                stream,
                6,
                13,
                false,
                "second\n"));
        Guid rotatedStream = Guid.NewGuid();
        ServerInstanceStateReportResult rotated = instance.RecordProcessState(
            ServerInstanceStatus.Stopped,
            null,
            null,
            CreatedAt.AddSeconds(4),
            logs: new ServerInstanceLogReport(
                ServerInstanceLogStatus.Available,
                source,
                rotatedStream,
                0,
                8,
                true,
                "rotated\n"));

        Assert.Equal(ServerInstanceStateReportResult.Succeeded, initial);
        Assert.Equal(ServerInstanceStateReportResult.Succeeded, append);
        Assert.Equal(ServerInstanceStateReportResult.Succeeded, retry);
        Assert.Equal(ServerInstanceStateReportResult.Succeeded, rotated);
        Assert.Equal("rotated\n", instance.LastLogContent);
        Assert.Equal(rotatedStream, instance.LastLogStreamId);
        Assert.Equal(8, instance.LastLogOffset);
        Assert.True(instance.LastLogChunkReset);
    }

    [Fact]
    public void ProjectZomboidLogsRejectWrongSourceGapsAndUnsafeContent()
    {
        ServerInstance instance = ServerInstance.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            CreateProjectZomboidConfiguration(@"C:\Data\PZ"),
            CreatedAt);
        string source = ServerLogSourceIdentifier.Create(
            instance.Profile,
            instance.DataDirectory)!;
        Guid stream = Guid.NewGuid();

        Assert.Equal(
            ServerInstanceStateReportResult.InvalidLogs,
            instance.RecordProcessState(
                ServerInstanceStatus.Stopped,
                null,
                null,
                CreatedAt.AddSeconds(1),
                logs: new ServerInstanceLogReport(
                    ServerInstanceLogStatus.Available,
                    new string('A', ServerLogSourceIdentifier.Length),
                    stream,
                    0,
                    4,
                    true,
                    "one\n")));

        instance.RecordProcessState(
            ServerInstanceStatus.Stopped,
            null,
            null,
            CreatedAt.AddSeconds(2),
            logs: new ServerInstanceLogReport(
                ServerInstanceLogStatus.Available,
                source,
                stream,
                0,
                4,
                true,
                "one\n"));

        Assert.Equal(
            ServerInstanceStateReportResult.InvalidLogs,
            instance.RecordProcessState(
                ServerInstanceStatus.Stopped,
                null,
                null,
                CreatedAt.AddSeconds(3),
                logs: new ServerInstanceLogReport(
                    ServerInstanceLogStatus.Available,
                    source,
                    stream,
                    9,
                    13,
                    false,
                    "gap\n")));
        Assert.Equal(
            ServerInstanceStateReportResult.InvalidLogs,
            instance.RecordProcessState(
                ServerInstanceStatus.Stopped,
                null,
                null,
                CreatedAt.AddSeconds(4),
                logs: new ServerInstanceLogReport(
                    ServerInstanceLogStatus.Available,
                    source,
                    stream,
                    4,
                    9,
                    false,
                    "bad\u001b\n")));
        Assert.Equal("one\n", instance.LastLogContent);
    }

    [Fact]
    public void ProjectZomboidLogWindowIsBoundedAndConfigurationChangeClearsIt()
    {
        ServerInstance instance = ServerInstance.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            CreateProjectZomboidConfiguration(@"C:\Data\PZ"),
            CreatedAt);
        string source = ServerLogSourceIdentifier.Create(
            instance.Profile,
            instance.DataDirectory)!;
        Guid stream = Guid.NewGuid();
        long offset = 0;

        for (int chunk = 0; chunk < 3; chunk++)
        {
            string content = string.Concat(
                Enumerable.Range(chunk * 200, 200)
                    .Select(index => $"line-{index:D4}-payload\n"));
            long nextOffset = offset + Encoding.UTF8.GetByteCount(content);
            ServerInstanceStateReportResult result = instance.RecordProcessState(
                ServerInstanceStatus.Stopped,
                null,
                null,
                CreatedAt.AddSeconds(chunk + 1),
                logs: new ServerInstanceLogReport(
                    ServerInstanceLogStatus.Available,
                    source,
                    stream,
                    offset,
                    nextOffset,
                    chunk == 0,
                    content));
            Assert.Equal(ServerInstanceStateReportResult.Succeeded, result);
            offset = nextOffset;
        }

        Assert.True(
            Encoding.UTF8.GetByteCount(instance.LastLogContent!) <=
            ServerInstanceLogReport.MaximumWindowBytes);
        Assert.True(
            instance.LastLogContent!.Count(character => character == '\n') <=
            ServerInstanceLogReport.MaximumWindowLines);
        Assert.True(instance.LastLogChunkReset);

        instance.UpdateConfiguration(
            CreateProjectZomboidConfiguration(@"C:\Data\PZ2"),
            CreatedAt.AddMinutes(1));

        Assert.Null(instance.LastLogStatus);
        Assert.Null(instance.LastLogContent);
        Assert.Null(instance.LastLogStreamId);
        Assert.Null(instance.LastLogReportedAt);
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

    private static ServerInstanceConfiguration CreateProjectZomboidConfiguration(
        string dataDirectory)
    {
        bool created = ServerInstanceConfiguration.TryCreate(
            ServerInstanceProfile.ProjectZomboid,
            "Project Zomboid",
            @"C:\Servers\PZ\StartServer64.bat",
            string.Empty,
            @"C:\Servers\PZ",
            "java",
            dataDirectory,
            out ServerInstanceConfiguration? configuration);

        Assert.True(created);
        return Assert.IsType<ServerInstanceConfiguration>(configuration);
    }
}

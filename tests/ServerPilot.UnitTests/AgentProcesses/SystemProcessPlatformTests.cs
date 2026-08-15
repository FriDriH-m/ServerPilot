using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using ServerPilot.Agent.Processes;

namespace ServerPilot.UnitTests.AgentProcesses;

public sealed class SystemProcessPlatformTests
{
    [Fact]
    public async Task StartsInspectsAndStopsHarmlessFixtureOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string executablePath = FindFixtureExecutable();
        LocalProcessConfigurationResult configuration = LocalProcessConfiguration.Create(
            executablePath,
            string.Empty,
            Path.GetDirectoryName(executablePath),
            "ServerPilot.ProcessFixture");
        Assert.True(configuration.IsValid);

        ProcessIdentity? startedIdentity = null;
        using LocalProcessSupervisor supervisor = new(
            Guid.NewGuid(),
            configuration.Configuration!,
            new SystemProcessPlatform(),
            NullLogger<LocalProcessSupervisor>.Instance,
            new ProcessStopTimeouts(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(5)));

        try
        {
            ProcessSupervisorResult started = await supervisor.StartAsync(CancellationToken.None);
            startedIdentity = started.Identity;
            ProcessSupervisorResult inspected = await supervisor.InspectAsync(CancellationToken.None);
            ProcessSupervisorResult stopped = await supervisor.StopAsync(CancellationToken.None);

            Assert.Equal(ProcessSupervisorStatus.Started, started.Status);
            Assert.Equal(ProcessSupervisorStatus.Running, inspected.Status);
            Assert.Equal(ProcessSupervisorStatus.Stopped, stopped.Status);
        }
        finally
        {
            KillFixtureIfStillRunning(startedIdentity);
        }
    }

    [Fact]
    public async Task ProjectZomboidProfileTracksJavaChildAndStopsThroughConsoleOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string testRoot = Path.Combine(
            Path.GetTempPath(),
            $"ServerPilot PZ (fixture) {Guid.NewGuid():N}");
        string javaDirectory = Path.Combine(testRoot, "jre64", "bin");
        string dataDirectory = Path.Combine(testRoot, "data");
        string configurationDirectory = Path.Combine(dataDirectory, "Server");
        Directory.CreateDirectory(javaDirectory);
        Directory.CreateDirectory(configurationDirectory);
        string launcherPath = Path.Combine(testRoot, "StartServer64.bat");
        string javaPath = Path.Combine(javaDirectory, "java.exe");
        File.WriteAllText(
            launcherPath,
            "@echo off\r\n\".\\jre64\\bin\\java.exe\" --stdin-quit " +
            "zombie.network.GameServer %1 %2\r\nPAUSE\r\n");
        File.WriteAllText(Path.Combine(configurationDirectory, "servertest.ini"), string.Empty);
        CopyFixtureOutput(javaDirectory, javaPath);

        LocalProcessConfigurationResult configuration = LocalProcessConfiguration.Create(
            "ProjectZomboid",
            launcherPath,
            string.Empty,
            testRoot,
            "java",
            dataDirectory);
        Assert.True(configuration.IsValid);

        ProcessIdentity? startedIdentity = null;
        try
        {
            using SystemProcessPlatform platform = new();
            using LocalProcessSupervisor supervisor = new(
                Guid.NewGuid(),
                configuration.Configuration!,
                platform,
                NullLogger<LocalProcessSupervisor>.Instance,
                new ProcessStopTimeouts(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(5)));

            ProcessSupervisorResult started = await supervisor.StartAsync(CancellationToken.None);
            startedIdentity = started.Identity;
            ProcessSupervisorResult inspected = await supervisor.InspectAsync(
                CancellationToken.None);
            ProcessSupervisorResult stopped = await supervisor.StopAsync(CancellationToken.None);

            Assert.Equal(ProcessSupervisorStatus.Started, started.Status);
            Assert.Equal(LocalServerProfile.ProjectZomboid, started.Identity?.Profile);
            Assert.Equal(javaPath, started.Identity?.ExecutablePath, ignoreCase: true);
            Assert.Equal(ProcessSupervisorStatus.Running, inspected.Status);
            Assert.Equal(ProcessSupervisorStatus.Stopped, stopped.Status);
            Assert.False(stopped.Forced);
        }
        finally
        {
            KillFixtureIfStillRunning(startedIdentity);
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ProjectZomboidProfileRejectsLauncherWithoutGameServerArgumentForwarding()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string testRoot = Path.Combine(
            Path.GetTempPath(),
            $"ServerPilot-PZ-invalid-{Guid.NewGuid():N}");
        string javaDirectory = Path.Combine(testRoot, "jre64", "bin");
        string dataDirectory = Path.Combine(testRoot, "data");
        string configurationDirectory = Path.Combine(dataDirectory, "Server");
        Directory.CreateDirectory(javaDirectory);
        Directory.CreateDirectory(configurationDirectory);
        string launcherPath = Path.Combine(testRoot, "StartServer64.bat");
        File.WriteAllText(
            launcherPath,
            "@echo off\r\nrem zombie.network.GameServer %1\r\nexit /b 0\r\n");
        File.WriteAllText(Path.Combine(javaDirectory, "java.exe"), string.Empty);
        File.WriteAllText(Path.Combine(configurationDirectory, "servertest.ini"), string.Empty);
        LocalProcessConfigurationResult configuration = LocalProcessConfiguration.Create(
            "ProjectZomboid",
            launcherPath,
            string.Empty,
            testRoot,
            "java",
            dataDirectory);

        try
        {
            using SystemProcessPlatform platform = new();
            using LocalProcessSupervisor supervisor = new(
                Guid.NewGuid(),
                configuration.Configuration!,
                platform,
                NullLogger<LocalProcessSupervisor>.Instance);

            ProcessSupervisorResult result = await supervisor.StartAsync(CancellationToken.None);

            Assert.Equal(ProcessSupervisorStatus.Failed, result.Status);
            Assert.Equal(ProcessSupervisorFailure.InvalidLauncher, result.Failure);
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    private static string FindFixtureExecutable()
    {
        string repositoryRoot = FindRepositoryRoot();
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug";
        return Path.Combine(
            repositoryRoot,
            "tests",
            "ServerPilot.ProcessFixture",
            "bin",
            configuration,
            "net10.0",
            "ServerPilot.ProcessFixture.exe");
    }

    private static void CopyFixtureOutput(string targetDirectory, string javaPath)
    {
        string executablePath = FindFixtureExecutable();
        string sourceDirectory = Path.GetDirectoryName(executablePath)!;
        foreach (string sourcePath in Directory.EnumerateFiles(
                     sourceDirectory,
                     "ServerPilot.ProcessFixture*"))
        {
            File.Copy(
                sourcePath,
                Path.Combine(targetDirectory, Path.GetFileName(sourcePath)));
        }

        File.Copy(executablePath, javaPath);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null &&
            !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException(
            "Could not locate the ServerPilot repository root.");
    }

    private static void KillFixtureIfStillRunning(ProcessIdentity? identity)
    {
        if (identity is null)
        {
            return;
        }

        try
        {
            using Process process = Process.GetProcessById(identity.ProcessId);
            DateTimeOffset startedAt = new(process.StartTime.ToUniversalTime());
            if (startedAt == identity.StartedAtUtc)
            {
                process.Kill();
                process.WaitForExit(TimeSpan.FromSeconds(5));
            }
        }
        catch (ArgumentException)
        {
            // The fixture already exited.
        }
        catch (InvalidOperationException)
        {
            // The fixture already exited.
        }
    }
}

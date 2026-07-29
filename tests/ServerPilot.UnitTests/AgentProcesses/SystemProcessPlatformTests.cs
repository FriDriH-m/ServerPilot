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

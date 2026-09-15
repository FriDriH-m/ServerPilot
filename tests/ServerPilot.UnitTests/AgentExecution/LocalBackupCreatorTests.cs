using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using ServerPilot.Agent.Api;
using ServerPilot.Agent.Backups;
using ServerPilot.Agent.Execution;
using ServerPilot.Agent.Processes;
using ServerPilot.Domain.Backups;

namespace ServerPilot.UnitTests.AgentExecution;

public sealed class LocalBackupCreatorTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "serverpilot-backup-tests", Guid.NewGuid().ToString("N"));
    private readonly string source;
    private readonly string destination;
    private readonly ClaimedAgentCommand command;

    public LocalBackupCreatorTests()
    {
        source = Directory.CreateDirectory(Path.Combine(root, "data")).FullName;
        destination = Directory.CreateDirectory(Path.Combine(root, "backups")).FullName;
        command = new ClaimedAgentCommand(Guid.NewGuid(), Guid.NewGuid(), AgentCommandType.CreateBackup,
            Guid.NewGuid(), "New", new ClaimedAgentServerInstance("ProjectZomboid", "unused", "", "unused",
                "sp-backup-test-absent", source));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public async Task CreatesReadableArchiveAndReplaysSameArtifactAfterRestart()
    {
        Directory.CreateDirectory(Path.Combine(source, "Saves"));
        await File.WriteAllTextAsync(Path.Combine(source, "Saves", "world.db"), "world-state");
        AgentCommandOutcome first = await Create().CreateAsync(command, new StoppedSupervisor(), CancellationToken.None);
        Assert.True(first.Succeeded, first.ErrorCode);
        string archive = ArchivePath();
        using (ZipArchive zip = ZipFile.OpenRead(archive))
        {
            using var reader = new StreamReader(zip.GetEntry("Saves/world.db")!.Open());
            Assert.Equal("world-state", await reader.ReadToEndAsync());
        }
        Assert.Equal(new FileInfo(archive).Length, first.Backup!.SizeBytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(archive))), first.Backup.Checksum);
        await File.WriteAllTextAsync(Path.Combine(source, "Saves", "world.db"), "later-world-state");
        AgentCommandOutcome replay = await Create().CreateAsync(command with { DeliveryKind = "Recovery" },
            new StoppedSupervisor(ProcessSupervisorStatus.Running), CancellationToken.None);
        Assert.Equal(first.Backup, replay.Backup);
        Assert.Single(Directory.GetFiles(destination, "*.zip", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(destination, "*.partial", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task CorruptedPublishedArchiveIsNotReportedAsSuccessfulOnReplay()
    {
        await File.WriteAllTextAsync(Path.Combine(source, "save.db"), "original");
        Assert.True((await Create().CreateAsync(command, new StoppedSupervisor(), CancellationToken.None)).Succeeded);
        using (ZipArchive zip = ZipFile.Open(ArchivePath(), ZipArchiveMode.Update))
        {
            zip.GetEntry("save.db")!.Delete();
            using var writer = new StreamWriter(zip.CreateEntry("save.db").Open());
            writer.Write("tampered");
        }
        AgentCommandOutcome replay = await Create().CreateAsync(command, new StoppedSupervisor(), CancellationToken.None);
        Assert.False(replay.Succeeded);
        Assert.Null(replay.Backup);
        Assert.True(File.Exists(ArchivePath()));
    }

    [Theory]
    [InlineData(ProcessSupervisorStatus.Running)]
    [InlineData(ProcessSupervisorStatus.Failed)]
    [InlineData(ProcessSupervisorStatus.StaleProcessId)]
    public async Task RequiresVerifiedStoppedProcess(ProcessSupervisorStatus status)
    {
        AgentCommandOutcome result = await Create().CreateAsync(command, new StoppedSupervisor(status), CancellationToken.None);
        Assert.Equal("BackupRequiresStoppedServer", result.ErrorCode);
        Assert.False(File.Exists(ArchivePath()));
    }

    [Fact]
    public async Task SourceByteLimitCleansPartialAndRecoveryCanRetry()
    {
        await File.WriteAllTextAsync(Path.Combine(source, "save.db"), "longer-than-limit");
        AgentCommandOutcome result = await Create(maxBytes: 1).CreateAsync(command, new StoppedSupervisor(), CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Empty(Directory.GetFiles(destination, "*.partial", SearchOption.AllDirectories));
        Assert.False(File.Exists(ArchivePath()));
        Assert.True((await Create().CreateAsync(command, new StoppedSupervisor(), CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task EntryLimitIncludesDirectories()
    {
        Directory.CreateDirectory(Path.Combine(source, "first"));
        Directory.CreateDirectory(Path.Combine(source, "second"));
        AgentCommandOutcome result = await Create(maxEntries: 1).CreateAsync(command, new StoppedSupervisor(), CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.False(File.Exists(ArchivePath()));
    }

    [Fact]
    public async Task CancellationCleansPartialAndDoesNotReportSuccess()
    {
        using var cancellation = new CancellationTokenSource();
        await File.WriteAllTextAsync(Path.Combine(source, "save.db"), "world");
        var supervisor = new StoppedSupervisor(onInspect: cancellation.Cancel);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create().CreateAsync(command, supervisor, cancellation.Token));
        Assert.False(File.Exists(ArchivePath()));
        Assert.Empty(Directory.GetFiles(destination, "*.partial", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task SourceChangeDuringFinalCheckFailsWithoutPublishing()
    {
        int inspections = 0;
        var supervisor = new StoppedSupervisor(onInspect: () => inspections++);
        supervisor.SecondStatus = ProcessSupervisorStatus.Running;
        AgentCommandOutcome result = await Create().CreateAsync(command, supervisor, CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal(2, inspections);
        Assert.False(File.Exists(ArchivePath()));
    }

    [Fact]
    public async Task LockedSourceFailsAndCleansPartial()
    {
        string file = Path.Combine(source, "save.db");
        await File.WriteAllTextAsync(file, "world");
        await using var locked = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        AgentCommandOutcome result = await Create().CreateAsync(command, new StoppedSupervisor(), CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Empty(Directory.GetFiles(destination, "*.partial", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task RejectsOverlappingRootsAndUnsupportedProfiles()
    {
        Assert.Equal("UnsafeBackupPath", (await Create(source).CreateAsync(command, new StoppedSupervisor(), CancellationToken.None)).ErrorCode);
        Assert.Equal("BackupNotConfigured", (await Create().CreateAsync(command with
        { ServerInstance = command.ServerInstance with { Profile = "Generic" } }, new StoppedSupervisor(), CancellationToken.None)).ErrorCode);
        Assert.Equal("BackupFileOperationFailed", (await Create(Path.Combine(destination, "..", "backups"))
            .CreateAsync(command, new StoppedSupervisor(), CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public async Task ExistingPartialIsRebuiltButConcurrentCommandLockIsRespected()
    {
        string folder = Directory.CreateDirectory(Path.GetDirectoryName(ArchivePath())!).FullName;
        string partial = Path.Combine(folder, $"{command.Id:N}.partial");
        await File.WriteAllTextAsync(partial, "interrupted-write");
        await using (var locked = new FileStream(Path.Combine(folder, $"{command.Id:N}.lock"), FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.False((await Create().CreateAsync(command, new StoppedSupervisor(), CancellationToken.None)).Succeeded);
            Assert.Equal("interrupted-write", await File.ReadAllTextAsync(partial));
        }
        Assert.True((await Create().CreateAsync(command, new StoppedSupervisor(), CancellationToken.None)).Succeeded);
        Assert.False(File.Exists(partial));
    }

    [Theory]
    [InlineData(0, 64)]
    [InlineData(1, 63)]
    [InlineData(107374182401, 64)]
    public void InvalidArtifactCannotComplete(long size, int checksumLength)
    {
        Assert.False(Backup.IsValidArtifact(size, new string('A', checksumLength)));
        Assert.False(Backup.Create(Guid.NewGuid()).TryComplete(size, new string('A', checksumLength)));
    }

    [Fact]
    public async Task PublicationFailureDoesNotLeaveASuccessfulOrPartialArchive()
    {
        Directory.CreateDirectory(ArchivePath());
        AgentCommandOutcome result = await Create().CreateAsync(command, new StoppedSupervisor(), CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Null(result.Backup);
        Assert.Empty(Directory.GetFiles(destination, "*.partial", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task LinkedDirectoryCannotEscapeSourceRoot()
    {
        string link = Path.Combine(source, "outside");
        // Windows junction creation does not require the symbolic-link privilege.
        if (OperatingSystem.IsWindows())
        {
            var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("/c");
            start.ArgumentList.Add("mklink");
            start.ArgumentList.Add("/J");
            start.ArgumentList.Add(link);
            start.ArgumentList.Add(destination);
            using System.Diagnostics.Process process = System.Diagnostics.Process.Start(start)!;
            await process.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);
        }
        else Directory.CreateSymbolicLink(link, destination);
        try
        {
            AgentCommandOutcome result = await Create().CreateAsync(command, new StoppedSupervisor(), CancellationToken.None);
            Assert.False(result.Succeeded);
            Assert.False(File.Exists(ArchivePath()));
        }
        finally { Directory.Delete(link); }
    }

    private string ArchivePath() => Path.Combine(destination, command.ServerInstanceId.ToString("N"), $"{command.Id:N}.zip");

    private LocalBackupCreator Create(string? target = null, long maxBytes = 1024 * 1024, int maxEntries = 100) =>
        new(new LocalBackupOptions { RootDirectory = target ?? destination, MaximumSourceBytes = maxBytes, MaximumEntries = maxEntries },
            NullLogger<LocalBackupCreator>.Instance);

    private sealed class StoppedSupervisor(ProcessSupervisorStatus status = ProcessSupervisorStatus.NotRunning, Action? onInspect = null) : IProcessSupervisor
    {
        private int inspections;
        public ProcessSupervisorStatus? SecondStatus { get; set; }
        public Task<ProcessSupervisorResult> InspectAsync(CancellationToken cancellationToken)
        {
            onInspect?.Invoke();
            return Task.FromResult(new ProcessSupervisorResult(++inspections == 2 ? SecondStatus ?? status : status));
        }
        public Task<ProcessSupervisorResult> StartAsync(CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task<ProcessSupervisorResult> StopAsync(CancellationToken cancellationToken) => throw new InvalidOperationException();
    }
}

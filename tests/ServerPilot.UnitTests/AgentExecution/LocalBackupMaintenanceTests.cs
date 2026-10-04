using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ServerPilot.Agent.Api;
using ServerPilot.Agent.Backups;
using ServerPilot.Agent.Processes;

namespace ServerPilot.UnitTests.AgentExecution;

public sealed class LocalBackupMaintenanceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "serverpilot-restore-tests", Guid.NewGuid().ToString("N"));
    private readonly string source;
    private readonly string destination;
    private readonly ClaimedAgentCommand backupCommand;
    private readonly LocalBackupCreator creator;
    private readonly LocalBackupMaintenance maintenance;

    public LocalBackupMaintenanceTests()
    {
        source = Directory.CreateDirectory(Path.Combine(root, "data")).FullName;
        destination = Directory.CreateDirectory(Path.Combine(root, "backups")).FullName;
        backupCommand = new(Guid.NewGuid(), Guid.NewGuid(), AgentCommandType.CreateBackup, Guid.NewGuid(), "New",
            new("ProjectZomboid", "unused", "", "unused", "sp-restore-test-absent", source));
        var options = new LocalBackupOptions { RootDirectory = destination, MaximumSourceBytes = 1024 * 1024, MaximumEntries = 100 };
        creator = new(options, NullLogger<LocalBackupCreator>.Instance);
        maintenance = new(options, creator);
    }

    public void Dispose() => Directory.Delete(root, true);
    private string Folder => Path.Combine(destination, backupCommand.ServerInstanceId.ToString("N"));
    private string Archive => Path.Combine(Folder, $"{backupCommand.Id:N}.zip");
    private string Marker => Path.Combine(Folder, "restore-recovery.json");

    private async Task<ClaimedAgentCommand> PrepareAsync()
    {
        await File.WriteAllTextAsync(Path.Combine(source, "save.db"), "saved world");
        var result = await creator.CreateAsync(backupCommand, new Supervisor(), CancellationToken.None);
        Assert.True(result.Succeeded, result.ErrorCode);
        await File.WriteAllTextAsync(Path.Combine(source, "save.db"), "current world");
        return backupCommand with
        {
            Id = Guid.NewGuid(),
            Type = AgentCommandType.RestoreBackup,
            BackupTargets = [new(backupCommand.Id, result.Backup!.SizeBytes, result.Backup.Checksum)]
        };
    }

    [Fact]
    public async Task RestoresVerifiedFilesPreservesOriginalAndReplaysWithoutOverwritingLaterChanges()
    {
        var command = await PrepareAsync();
        var result = await maintenance.ExecuteAsync(command, new Supervisor(), (_, _) => Task.CompletedTask, CancellationToken.None);
        Assert.True(result.Succeeded, result.ErrorCode);
        Assert.Equal("saved world", await File.ReadAllTextAsync(Path.Combine(source, "save.db")));
        string original = Assert.Single(Directory.GetDirectories(root, "*.original"));
        Assert.Equal("current world", await File.ReadAllTextAsync(Path.Combine(original, "save.db")));
        Assert.False(maintenance.HasRecoveryPending(command.ServerInstanceId));
        await File.WriteAllTextAsync(Path.Combine(source, "save.db"), "later world");
        Assert.True((await maintenance.ExecuteAsync(command, new Supervisor(ProcessSupervisorStatus.Running),
            (_, _) => Task.CompletedTask, CancellationToken.None)).Succeeded);
        Assert.Equal("later world", await File.ReadAllTextAsync(Path.Combine(source, "save.db")));
    }

    [Fact]
    public async Task CorruptChecksumCannotChangeCurrentData()
    {
        var command = await PrepareAsync();
        await File.AppendAllTextAsync(Archive, "tampered");
        var result = await maintenance.ExecuteAsync(command, new Supervisor(), (_, _) => Task.CompletedTask, CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal("current world", await File.ReadAllTextAsync(Path.Combine(source, "save.db")));
        Assert.False(File.Exists(Marker));
        Assert.Empty(Directory.GetDirectories(root, "*.original"));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("/absolute")]
    [InlineData("C:/outside")]
    [InlineData("sub\\escape")]
    [InlineData("save.db:stream")]
    [InlineData("CON")]
    [InlineData("save.db.")]
    public async Task UnsafeArchiveNamesAreRejectedBeforeChangingData(string name)
    {
        var command = await PrepareAsync();
        using (var zip = ZipFile.Open(Archive, ZipArchiveMode.Update))
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open());
            writer.Write("unsafe");
        }
        byte[] bytes = await File.ReadAllBytesAsync(Archive);
        command = command with { BackupTargets = [new(backupCommand.Id, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)))] };
        var result = await maintenance.ExecuteAsync(command, new Supervisor(), (_, _) => Task.CompletedTask, CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal("current world", await File.ReadAllTextAsync(Path.Combine(source, "save.db")));
        Assert.False(File.Exists(Marker));
        Assert.False(File.Exists(Path.Combine(root, "escape")));
    }

    [Theory]
    [InlineData(ProcessSupervisorStatus.Running)]
    [InlineData(ProcessSupervisorStatus.Failed)]
    [InlineData(ProcessSupervisorStatus.StaleProcessId)]
    public async Task RestoreRequiresVerifiedStoppedServer(ProcessSupervisorStatus status)
    {
        var command = await PrepareAsync();
        var result = await maintenance.ExecuteAsync(command, new Supervisor(status), (_, _) => Task.CompletedTask, CancellationToken.None);
        Assert.Equal("RestoreRequiresStoppedServer", result.ErrorCode);
        Assert.False(File.Exists(Marker));
    }

    [Fact]
    public async Task CancellationAfterStagingLeavesOriginalDataAndBlocksStartUntilRecovery()
    {
        var command = await PrepareAsync();
        using var cancellation = new CancellationTokenSource();
        var supervisor = new Supervisor(onSecondInspection: cancellation.Cancel);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => maintenance.ExecuteAsync(command, supervisor,
            (_, _) => Task.CompletedTask, cancellation.Token));
        Assert.Equal("current world", await File.ReadAllTextAsync(Path.Combine(source, "save.db")));
        Assert.True(maintenance.HasRecoveryPending(command.ServerInstanceId));
        Assert.Single(Directory.GetDirectories(root, "*.staging"));
    }

    [Fact]
    public async Task InterruptedFirstRenameRollsBackAndDoesNotReportRestoreSuccess()
    {
        var command = await PrepareAsync();
        await WritePreparedAsync(command);
        string prefix = Path.Combine(root, $".serverpilot-{command.ServerInstanceId:N}-{command.Id:N}");
        Directory.CreateDirectory(prefix + ".staging");
        Directory.Move(source, prefix + ".original");
        var result = await maintenance.ExecuteAsync(command, new Supervisor(), (_, _) => Task.CompletedTask, CancellationToken.None);
        Assert.Equal("RestoreRolledBack", result.ErrorCode);
        Assert.Equal("current world", await File.ReadAllTextAsync(Path.Combine(source, "save.db")));
        Assert.False(maintenance.HasRecoveryPending(command.ServerInstanceId));
    }

    [Fact]
    public async Task InterruptedCompletionRecordsSuccessWithoutApplyingAgain()
    {
        var command = await PrepareAsync();
        await WritePreparedAsync(command);
        string original = Path.Combine(root, $".serverpilot-{command.ServerInstanceId:N}-{command.Id:N}.original");
        Directory.Move(source, original);
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "save.db"), "restored once");
        var result = await maintenance.ExecuteAsync(command, new Supervisor(), (_, _) => Task.CompletedTask, CancellationToken.None);
        Assert.True(result.Succeeded, result.ErrorCode);
        Assert.Equal("restored once", await File.ReadAllTextAsync(Path.Combine(source, "save.db")));
        Assert.False(File.Exists(Marker));
    }

    [Fact]
    public async Task RetentionDeletesOnlyVerifiedTargetsAndReplaysLostAcknowledgement()
    {
        var command = (await PrepareAsync()) with { Type = AgentCommandType.PruneBackups };
        string unrelated = Path.Combine(Folder, "user.zip");
        await File.WriteAllTextAsync(unrelated, "unrelated");
        int reports = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => maintenance.ExecuteAsync(command, new Supervisor(),
            (_, _) => throw new InvalidOperationException("lost response"), CancellationToken.None));
        Assert.False(File.Exists(Archive));
        var result = await maintenance.ExecuteAsync(command, new Supervisor(), (id, _) =>
        { Assert.Equal(backupCommand.Id, id); reports++; return Task.CompletedTask; }, CancellationToken.None);
        Assert.True(result.Succeeded, result.ErrorCode);
        Assert.Equal(1, reports);
        Assert.Equal("unrelated", await File.ReadAllTextAsync(unrelated));
        Assert.Equal("current world", await File.ReadAllTextAsync(Path.Combine(source, "save.db")));
    }

    [Fact]
    public async Task RetentionDoesNotDeleteCorruptOrReplacedArchive()
    {
        var command = (await PrepareAsync()) with { Type = AgentCommandType.PruneBackups };
        await File.WriteAllTextAsync(Archive, "unrelated replacement");
        var result = await maintenance.ExecuteAsync(command, new Supervisor(), (_, _) => throw new InvalidOperationException(), CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal("unrelated replacement", await File.ReadAllTextAsync(Archive));
    }

    private async Task WritePreparedAsync(ClaimedAgentCommand command)
    {
        string json = JsonSerializer.Serialize(new
        {
            CommandId = command.Id,
            command.ServerInstanceId,
            BackupId = backupCommand.Id,
            SourceIdentifier = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))),
            State = "Prepared"
        });
        await File.WriteAllTextAsync(Marker, json);
        await File.WriteAllTextAsync(Path.Combine(Folder, $"restore-{command.Id:N}.json"), json);
    }

    private sealed class Supervisor(ProcessSupervisorStatus status = ProcessSupervisorStatus.NotRunning, Action? onSecondInspection = null) : IProcessSupervisor
    {
        private int count;
        public Task<ProcessSupervisorResult> InspectAsync(CancellationToken token)
        {
            if (++count == 2) onSecondInspection?.Invoke();
            return Task.FromResult(new ProcessSupervisorResult(status));
        }
        public Task<ProcessSupervisorResult> StartAsync(CancellationToken token) => throw new InvalidOperationException();
        public Task<ProcessSupervisorResult> StopAsync(CancellationToken token) => throw new InvalidOperationException();
    }
}

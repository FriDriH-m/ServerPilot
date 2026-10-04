using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ServerPilot.Agent.Api;
using ServerPilot.Agent.Execution;
using ServerPilot.Agent.Processes;

namespace ServerPilot.Agent.Backups;

public interface ILocalBackupMaintenance
{
    bool HasRecoveryPending(Guid serverId);
    Task<AgentCommandOutcome> ExecuteAsync(ClaimedAgentCommand command, IProcessSupervisor supervisor,
        Func<Guid, CancellationToken, Task> confirmDeletion, CancellationToken token);
}

public sealed class LocalBackupMaintenance(LocalBackupOptions options, LocalBackupCreator creator) : ILocalBackupMaintenance
{
    private const string RecoveryMarker = "restore-recovery.json";

    public bool HasRecoveryPending(Guid serverId)
    {
        if (string.IsNullOrWhiteSpace(options.RootDirectory)) return false;
        try
        {
            string root = LocalBackupCreator.ValidateDirectory(options.RootDirectory);
            string directory = Path.Combine(root, serverId.ToString("N"));
            if (!Directory.Exists(directory)) return false;
            LocalBackupCreator.ValidateDirectory(directory);
            return Path.Exists(Path.Combine(directory, RecoveryMarker));
        }
        catch (Exception ex) when (IsFileFailure(ex)) { return true; }
    }

    public async Task<AgentCommandOutcome> ExecuteAsync(ClaimedAgentCommand command, IProcessSupervisor supervisor,
        Func<Guid, CancellationToken, Task> confirmDeletion, CancellationToken token)
    {
        if (command.Id == Guid.Empty || command.ServerInstanceId == Guid.Empty ||
            command.ServerInstance.Profile != "ProjectZomboid" || string.IsNullOrWhiteSpace(command.ServerInstance.DataDirectory) ||
            string.IsNullOrWhiteSpace(options.RootDirectory) || command.BackupTargets is not { Count: > 0 and <= 1000 } targets ||
            targets.Select(item => item.Id).Distinct().Count() != targets.Count ||
            targets.Any(item => item.Id == Guid.Empty || item.SizeBytes is <= 0 or > 107374182400 ||
                item.Checksum is not { Length: 64 } || !item.Checksum.All(Uri.IsHexDigit)) ||
            (command.Type == AgentCommandType.RestoreBackup && targets.Count != 1)) return Failed("InvalidBackupOperation");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        try
        {
            string root = LocalBackupCreator.ValidateDirectory(options.RootDirectory);
            string directory = LocalBackupCreator.ValidateDirectory(Path.Combine(root, command.ServerInstanceId.ToString("N")));
            // Validate the parent even when an interrupted rename left the data directory absent.
            string source = Path.GetFullPath(command.ServerInstance.DataDirectory).TrimEnd(Path.DirectorySeparatorChar);
            LocalBackupCreator.ValidateDirectory(Path.GetDirectoryName(source)!);
            if (LocalBackupCreator.IsWithin(source, root) || LocalBackupCreator.IsWithin(root, source)) return Failed("UnsafeBackupPath");
            string lockPath = Path.Combine(directory, "maintenance.lock");
            LocalBackupCreator.RejectLinkIfExists(lockPath);
            await using var operationLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            BackupFileSafety.RejectHardLinks(operationLock.SafeFileHandle);
            if (command.Type == AgentCommandType.RestoreBackup)
                return await RestoreAsync(command, supervisor, directory, source, targets[0], deadline.Token);
            if (command.Type != AgentCommandType.PruneBackups || HasRecoveryPending(command.ServerInstanceId))
                return Failed("RestoreRecoveryRequired");
            LocalBackupCreator.ValidateDirectory(source);
            foreach (var target in targets)
            {
                deadline.Token.ThrowIfCancellationRequested();
                string archive = Path.Combine(directory, $"{target.Id:N}.zip");
                LocalBackupCreator.RejectLinkIfExists(archive);
                if (File.Exists(archive))
                {
                    // Hold the verified file open with delete sharing, but no write sharing, through deletion.
                    await using var input = await OpenVerifiedAsync(archive, source, command.ServerInstanceId, target, deadline.Token, FileShare.Read | FileShare.Delete);
                    File.Delete(archive);
                }
                else if (Path.Exists(archive)) throw new IOException("Expected an archive file.");
                await confirmDeletion(target.Id, deadline.Token);
            }
            return new(true, null, null, null);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return Failed("BackupMaintenanceTimedOut"); }
        catch (Exception ex) when (IsFileFailure(ex))
        {
            return Failed(HasRecoveryPending(command.ServerInstanceId) ? "RestoreRecoveryRequired" : "BackupMaintenanceFailed");
        }
    }

    private async Task<AgentCommandOutcome> RestoreAsync(ClaimedAgentCommand command, IProcessSupervisor supervisor,
        string directory, string source, AgentBackupReference target, CancellationToken token)
    {
        string receipt = Path.Combine(directory, $"restore-{command.Id:N}.json");
        string marker = Path.Combine(directory, RecoveryMarker);
        string prefix = Path.Combine(Path.GetDirectoryName(source)!, $".serverpilot-{command.ServerInstanceId:N}-{command.Id:N}");
        string staging = prefix + ".staging";
        string original = prefix + ".original";
        string identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
        var expected = new RestoreJournal(command.Id, command.ServerInstanceId, target.Id, identity, "Prepared");
        if (File.Exists(receipt))
        {
            var journal = await ReadJournalAsync(receipt, token);
            if (journal with { State = "Prepared" } != expected) return Failed("RestoreRecoveryRequired");
            if (journal.State == "Completed")
            {
                await ClearOwnMarkerAsync(marker, expected, token);
                return new(true, null, null, null);
            }
            if (journal.State == "RolledBack") return Failed("RestoreRolledBack");
            if (journal.State != "Prepared") return Failed("RestoreRecoveryRequired");
        }
        if (Path.Exists(marker))
        {
            if (await ReadJournalAsync(marker, token) != expected) return Failed("RestoreRecoveryRequired");
            if (!await LocalBackupCreator.IsStoppedAsync(command, supervisor, token)) return Failed("RestoreRequiresStoppedServer");
            // A crash between the two renames is rolled back, never silently continued.
            if (Directory.Exists(original) && !Path.Exists(source))
            {
                LocalBackupCreator.ValidateDirectory(original);
                Directory.Move(original, source);
                await WriteJournalAsync(receipt, expected with { State = "RolledBack" }, token);
                await ClearOwnMarkerAsync(marker, expected, token);
                return Failed("RestoreRolledBack");
            }
            // A crash after publication but before the completion receipt must not reapply the restore.
            if (Directory.Exists(original) && Directory.Exists(source) && !Path.Exists(staging))
            {
                LocalBackupCreator.ValidateDirectory(source);
                LocalBackupCreator.ValidateDirectory(original);
                await WriteJournalAsync(receipt, expected with { State = "Completed" }, token);
                await ClearOwnMarkerAsync(marker, expected, token);
                return new(true, null, null, null);
            }
            return Failed("RestoreRecoveryRequired");
        }
        if (Path.Exists(staging) || Path.Exists(original)) return Failed("RestoreRecoveryRequired");
        LocalBackupCreator.ValidateDirectory(source);
        if (!await LocalBackupCreator.IsStoppedAsync(command, supervisor, token)) return Failed("RestoreRequiresStoppedServer");
        string archive = Path.Combine(directory, $"{target.Id:N}.zip");
        await using var input = await OpenVerifiedAsync(archive, source, command.ServerInstanceId, target, token);
        // Validate the existing tree before renaming it; never move a tree containing links.
        foreach (string file in creator.EnumerateSource(source, token))
        {
            await using var existing = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            BackupFileSafety.RejectHardLinks(existing.SafeFileHandle);
        }
        await WriteJournalAsync(receipt, expected, token);
        await WriteJournalAsync(marker, expected, token);
        Directory.CreateDirectory(staging);
        using (var zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true))
        {
            foreach (var entry in zip.Entries)
            {
                token.ThrowIfCancellationRequested();
                if (entry.FullName == LocalBackupCreator.ManifestName) continue;
                LocalBackupCreator.ValidateEntryName(entry.FullName);
                if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 ||
                    (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Linked archive entries are not allowed.");
                string path = Path.GetFullPath(Path.Combine(staging, entry.FullName));
                if (!LocalBackupCreator.IsWithin(path, staging)) throw new InvalidDataException("Unsafe extraction path.");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                LocalBackupCreator.ValidateDirectory(Path.GetDirectoryName(path)!);
                await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);
                await using var content = entry.Open();
                await content.CopyToAsync(output, 65536, token);
                await output.FlushAsync(token);
                output.Flush(true);
            }
        }
        if (!await LocalBackupCreator.IsStoppedAsync(command, supervisor, token)) return Failed("RestoreRequiresStoppedServer");
        token.ThrowIfCancellationRequested();
        LocalBackupCreator.ValidateDirectory(source);
        LocalBackupCreator.ValidateDirectory(staging);
        Directory.Move(source, original);
        try { Directory.Move(staging, source); }
        catch (IOException)
        {
            if (!Path.Exists(source))
            {
                Directory.Move(original, source);
                await WriteJournalAsync(receipt, expected with { State = "RolledBack" }, CancellationToken.None);
                await ClearOwnMarkerAsync(marker, expected, CancellationToken.None);
            }
            throw;
        }
        // Do not cancel between publication and recording its outcome.
        await WriteJournalAsync(receipt, expected with { State = "Completed" }, CancellationToken.None);
        await ClearOwnMarkerAsync(marker, expected, CancellationToken.None);
        return new(true, null, null, null);
    }

    private async Task<FileStream> OpenVerifiedAsync(string path, string source, Guid serverId,
        AgentBackupReference target, CancellationToken token, FileShare sharing = FileShare.Read)
    {
        LocalBackupCreator.RejectLinkIfExists(path);
        var input = new FileStream(path, FileMode.Open, FileAccess.Read, sharing, 65536, FileOptions.Asynchronous);
        try
        {
            BackupFileSafety.RejectHardLinks(input.SafeFileHandle);
            if (input.Length != target.SizeBytes || !string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(input, token)), target.Checksum, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Backup checksum mismatch.");
            string sourceId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
            input.Position = 0;
            await creator.ReadArtifactAsync(input, new(target.Id, serverId, sourceId), token);
            input.Position = 0;
            return input;
        }
        catch { await input.DisposeAsync(); throw; }
    }

    private static async Task<RestoreJournal> ReadJournalAsync(string path, CancellationToken token)
    {
        LocalBackupCreator.RejectLinkIfExists(path);
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        BackupFileSafety.RejectHardLinks(input.SafeFileHandle);
        if (input.Length > 4096) throw new InvalidDataException("Invalid restore journal.");
        return await JsonSerializer.DeserializeAsync<RestoreJournal>(input, cancellationToken: token)
            ?? throw new InvalidDataException("Invalid restore journal.");
    }

    private static async Task WriteJournalAsync(string path, RestoreJournal journal, CancellationToken token)
    {
        LocalBackupCreator.RejectLinkIfExists(path);
        string temporary = path + ".tmp";
        LocalBackupCreator.RejectLinkIfExists(temporary);
        await using (var output = new FileStream(temporary, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
        {
            BackupFileSafety.RejectHardLinks(output.SafeFileHandle);
            output.SetLength(0);
            await JsonSerializer.SerializeAsync(output, journal, cancellationToken: token);
            await output.FlushAsync(token);
            output.Flush(true);
        }
        File.Move(temporary, path, overwrite: true);
    }

    private static async Task ClearOwnMarkerAsync(string marker, RestoreJournal expected, CancellationToken token)
    {
        if (!Path.Exists(marker)) return;
        if (await ReadJournalAsync(marker, token) != expected) throw new InvalidDataException("Restore marker mismatch.");
        File.Delete(marker);
    }

    private static bool IsFileFailure(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException or
        System.Security.SecurityException or JsonException or ArgumentException or NotSupportedException;
    private static AgentCommandOutcome Failed(string code) => AgentCommandOutcome.Failed(code,
        "Backup maintenance did not complete. Check the Agent restore journal and documented recovery procedure before retrying.");
    private sealed record RestoreJournal(Guid CommandId, Guid ServerInstanceId, Guid BackupId, string SourceIdentifier, string State);
}

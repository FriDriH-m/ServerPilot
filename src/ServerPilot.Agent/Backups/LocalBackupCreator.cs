using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ServerPilot.Agent.Api;
using ServerPilot.Agent.Execution;
using ServerPilot.Agent.Processes;

namespace ServerPilot.Agent.Backups;

public interface ILocalBackupCreator
{
    Task<AgentCommandOutcome> CreateAsync(ClaimedAgentCommand command,
        IProcessSupervisor supervisor, CancellationToken cancellationToken);
}

public sealed partial class LocalBackupCreator(LocalBackupOptions options, ILogger<LocalBackupCreator> logger)
    : ILocalBackupCreator
{
    private const string ManifestName = ".serverpilot-backup.json";
    private const int BufferSize = 64 * 1024;

    public async Task<AgentCommandOutcome> CreateAsync(ClaimedAgentCommand command,
        IProcessSupervisor supervisor, CancellationToken cancellationToken)
    {
        if (command.Type != AgentCommandType.CreateBackup || command.Id == Guid.Empty ||
            command.ServerInstanceId == Guid.Empty || command.ServerInstance.Profile != "ProjectZomboid" ||
            string.IsNullOrWhiteSpace(command.ServerInstance.DataDirectory) ||
            string.IsNullOrWhiteSpace(options.RootDirectory))
            return Failed("BackupNotConfigured");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        CancellationToken token = deadline.Token;
        string? partial = null;
        FileStream? commandLock = null;
        try
        {
            string source = ValidateDirectory(command.ServerInstance.DataDirectory);
            string root = ValidateDirectory(options.RootDirectory);
            if (IsWithin(source, root) || IsWithin(root, source)) return Failed("UnsafeBackupPath");
            string destination = Path.Combine(root, command.ServerInstanceId.ToString("N"));
            if (Path.Exists(destination)) ValidateDirectory(destination);
            else Directory.CreateDirectory(destination);
            ValidateDirectory(destination);
            string archivePath = Path.Combine(destination, $"{command.Id:N}.zip");
            string lockPath = Path.Combine(destination, $"{command.Id:N}.lock");
            RejectLinkIfExists(lockPath);
            commandLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            RejectLinkIfExists(archivePath);
            string sourceId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
            Manifest manifest = new(command.Id, command.ServerInstanceId, sourceId);
            if (File.Exists(archivePath)) return await ReadArtifactAsync(archivePath, manifest, token);
            if (!await IsStoppedAsync(command, supervisor, token)) return Failed("BackupRequiresStoppedServer");

            partial = Path.Combine(destination, $"{command.Id:N}.partial");
            RejectLinkIfExists(partial);
            File.Delete(partial);
            Dictionary<string, FileStamp> files = new(StringComparer.Ordinal);
            long totalBytes = 0;
            byte[] buffer = new byte[BufferSize];
            using var contentHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.None, BufferSize, FileOptions.Asynchronous))
            {
                using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
                {
                    foreach (string path in EnumerateSource(source, token))
                    {
                        string relative = Path.GetRelativePath(source, path).Replace(Path.DirectorySeparatorChar, '/');
                        ValidateEntryName(relative);
                        if (files.Count >= options.MaximumEntries) throw new InvalidDataException("Backup entry limit exceeded.");
                        FileStamp before = Stamp(path);
                        totalBytes = checked(totalBytes + before.Length);
                        if (totalBytes > options.MaximumSourceBytes) throw new InvalidDataException("Backup byte limit exceeded.");
                        files.Add(relative, before);
                        AppendEntryIdentity(contentHash, relative, before.Length);
                        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read,
                            FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
                        BackupFileSafety.RejectHardLinks(input.SafeFileHandle);
                        await using Stream entry = zip.CreateEntry(relative, CompressionLevel.Fastest).Open();
                        long copied = 0;
                        int count;
                        while ((count = await input.ReadAsync(buffer, token)) != 0)
                        {
                            copied += count;
                            if (copied > before.Length) throw new InvalidDataException("Source changed during backup.");
                            await entry.WriteAsync(buffer.AsMemory(0, count), token);
                            contentHash.AppendData(buffer, 0, count);
                        }
                        if (copied != before.Length || Stamp(path) != before)
                            throw new InvalidDataException("Source changed during backup.");
                    }
                    await using Stream marker = zip.CreateEntry(ManifestName).Open();
                    await JsonSerializer.SerializeAsync(marker,
                        manifest with { ContentChecksum = Convert.ToHexString(contentHash.GetHashAndReset()) },
                        cancellationToken: token);
                }
                await output.FlushAsync(token);
                output.Flush(flushToDisk: true);
            }

            int verified = 0;
            foreach (string path in EnumerateSource(source, token))
            {
                string relative = Path.GetRelativePath(source, path).Replace(Path.DirectorySeparatorChar, '/');
                if (!files.TryGetValue(relative, out FileStamp? before) || Stamp(path) != before)
                    throw new InvalidDataException("Source changed during backup.");
                verified++;
            }
            if (verified != files.Count || !await IsStoppedAsync(command, supervisor, token))
                return Failed("BackupSourceChanged");
            AgentCommandOutcome result = await ReadArtifactAsync(partial, manifest, token);
            token.ThrowIfCancellationRequested();
            File.Move(partial, archivePath, overwrite: false);
            partial = null;
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed("BackupTimedOut");
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or
            System.Security.SecurityException or JsonException or ArgumentException or NotSupportedException)
        {
            LogBackupFailure(logger, command.Id, command.ServerInstanceId, exception.GetType().Name);
            return Failed("BackupFileOperationFailed");
        }
        finally
        {
            if (partial is not null)
            {
                try { File.Delete(partial); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    LogCleanupFailure(logger, command.Id);
                }
            }
            if (commandLock is not null) await commandLock.DisposeAsync();
        }
    }

    private async Task<AgentCommandOutcome> ReadArtifactAsync(string path, Manifest expected, CancellationToken token)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (input.Length is <= 0 or > 100L * 1024 * 1024 * 1024)
            throw new InvalidDataException("Invalid archive size.");
        using (var zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true))
        {
            if (zip.Entries.Count > options.MaximumEntries + 1) throw new InvalidDataException("Invalid archive entry count.");
            ZipArchiveEntry? marker = zip.GetEntry(ManifestName);
            if (marker is null || marker.Length > 1024) throw new InvalidDataException("Missing backup identity.");
            await using Stream data = marker.Open();
            Manifest? actual = await JsonSerializer.DeserializeAsync<Manifest>(data, cancellationToken: token);
            if (actual is null || actual with { ContentChecksum = null } != expected)
                throw new InvalidDataException("Backup identity mismatch.");
            using var contentHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            byte[] buffer = new byte[BufferSize];
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                token.ThrowIfCancellationRequested();
                if (!names.Add(entry.FullName)) throw new InvalidDataException("Duplicate archive entry.");
                if (entry.FullName == ManifestName) continue;
                ValidateEntryName(entry.FullName);
                total = checked(total + entry.Length);
                if (entry.Length < 0 || total > options.MaximumSourceBytes)
                    throw new InvalidDataException("Archive byte limit exceeded.");
                AppendEntryIdentity(contentHash, entry.FullName, entry.Length);
                await using Stream contents = entry.Open();
                long copied = 0;
                int count;
                while ((count = await contents.ReadAsync(buffer, token)) != 0)
                {
                    copied += count;
                    if (copied > entry.Length) throw new InvalidDataException("Invalid archive entry size.");
                    contentHash.AppendData(buffer, 0, count);
                }
                if (copied != entry.Length) throw new InvalidDataException("Incomplete archive entry.");
            }
            if (Convert.ToHexString(contentHash.GetHashAndReset()) != actual.ContentChecksum)
                throw new InvalidDataException("Archive contents do not match the manifest.");
        }
        input.Position = 0;
        string checksum = Convert.ToHexString(await SHA256.HashDataAsync(input, token));
        return new AgentCommandOutcome(true, null, null, null, new BackupArtifact(input.Length, checksum));
    }

    private IEnumerable<string> EnumerateSource(string root, CancellationToken token)
    {
        int entries = 0;
        IEnumerable<string> Walk(string directory, int depth)
        {
            if (depth > 64) throw new InvalidDataException("Backup directory depth exceeded.");
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                token.ThrowIfCancellationRequested();
                if (++entries > options.MaximumEntries) throw new InvalidDataException("Backup entry limit exceeded.");
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked paths are not allowed.");
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    foreach (string file in Walk(path, depth + 1)) yield return file;
                }
                else yield return path;
            }
        }
        return Walk(root, 0);
    }

    private static async Task<bool> IsStoppedAsync(ClaimedAgentCommand command,
        IProcessSupervisor supervisor, CancellationToken token)
    {
        if ((await supervisor.InspectAsync(token)).Status != ProcessSupervisorStatus.NotRunning) return false;
        Process[] processes = Process.GetProcessesByName(command.ServerInstance.ProcessName);
        try { return processes.Length == 0; }
        finally { foreach (Process process in processes) process.Dispose(); }
    }

    private static FileStamp Stamp(string path)
    {
        RejectLinkIfExists(path);
        FileInfo info = new(path);
        return new FileStamp(info.Length, info.LastWriteTimeUtc);
    }

    private static string ValidateDirectory(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal) ||
            path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "." or ".."))
            throw new IOException("An absolute local path without traversal is required.");
        string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(full, Path.GetPathRoot(full)?.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new IOException("A volume root is not a backup directory.");
        DirectoryInfo? directory = new(full);
        if (!directory.Exists) throw new DirectoryNotFoundException();
        while (directory is not null)
        {
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked paths are not allowed.");
            directory = directory.Parent;
        }
        return full;
    }

    private static bool IsWithin(string path, string root) =>
        string.Equals(path, root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static void RejectLinkIfExists(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked paths are not allowed.");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Local backup {CommandId} for server {ServerInstanceId} failed with {FailureType}")]
    private static partial void LogBackupFailure(ILogger logger, Guid commandId, Guid serverInstanceId, string failureType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Partial backup cleanup failed for command {CommandId}; retry or operator cleanup is required")]
    private static partial void LogCleanupFailure(ILogger logger, Guid commandId);

    private static AgentCommandOutcome Failed(string code) =>
        AgentCommandOutcome.Failed(code, "Local backup could not be completed. Check the Agent configuration, stopped state, file access and disk space.");

    private static void ValidateEntryName(string name)
    {
        if (name == ManifestName || name.Contains('\\') || name.Contains(':') || name.Length > 1024 ||
            name.Split('/').Any(part => part is "." or ".." or ""))
            throw new InvalidDataException("Unsafe archive entry.");
    }

    private static void AppendEntryIdentity(IncrementalHash hash, string name, long length)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(name);
        Span<byte> prefix = stackalloc byte[12];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(prefix, bytes.Length);
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(prefix[4..], length);
        hash.AppendData(prefix);
        hash.AppendData(bytes);
    }

    private sealed record Manifest(Guid CommandId, Guid ServerInstanceId, string SourceIdentifier, string? ContentChecksum = null);
    private sealed record FileStamp(long Length, DateTime LastWriteTimeUtc);
}

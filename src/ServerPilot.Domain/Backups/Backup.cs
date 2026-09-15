namespace ServerPilot.Domain.Backups;

public enum BackupStatus
{
    Pending = 1,
    Running = 2,
    Completed = 3,
    Failed = 4,
}

public sealed class Backup
{
    public const long MaximumArchiveBytes = 100L * 1024 * 1024 * 1024;

    private Backup() { }

    public Guid Id { get; private set; }
    public BackupStatus Status { get; private set; }
    public long? SizeBytes { get; private set; }
    public string? Checksum { get; private set; }

    public static Backup Create(Guid commandId)
    {
        if (commandId == Guid.Empty)
        {
            throw new ArgumentException("Command ID is required.", nameof(commandId));
        }

        return new Backup { Id = commandId, Status = BackupStatus.Pending };
    }

    public static bool IsValidArtifact(long sizeBytes, string? checksum) =>
        sizeBytes is > 0 and <= MaximumArchiveBytes &&
        checksum is { Length: 64 } && checksum.All(Uri.IsHexDigit);

    public static bool CanCreate(ServerInstances.ServerInstance instance) =>
        instance.Profile == ServerInstances.ServerInstanceProfile.ProjectZomboid &&
        instance.Status == ServerInstances.ServerInstanceStatus.Stopped &&
        instance.DataDirectory is not null;

    public bool TryComplete(long sizeBytes, string checksum)
    {
        if (Status != BackupStatus.Running || !IsValidArtifact(sizeBytes, checksum))
        {
            return false;
        }

        SizeBytes = sizeBytes;
        Checksum = checksum.ToUpperInvariant();
        Status = BackupStatus.Completed;
        return true;
    }
}

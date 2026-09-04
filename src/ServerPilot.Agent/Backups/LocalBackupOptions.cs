namespace ServerPilot.Agent.Backups;

public sealed class LocalBackupOptions
{
    public const string SectionName = "Backups";
    public string? RootDirectory { get; init; }
    public long MaximumSourceBytes { get; init; } = 8L * 1024 * 1024 * 1024;
    public int MaximumEntries { get; init; } = 100_000;
    public int TimeoutSeconds { get; init; } = 1800;

    public void Validate()
    {
        if (MaximumSourceBytes is < 1 or > 100L * 1024 * 1024 * 1024 ||
            MaximumEntries is < 1 or > 100_000 || TimeoutSeconds is < 1 or > 3600)
            throw new InvalidOperationException("Backup resource limits are outside the allowed range.");
        if (!string.IsNullOrWhiteSpace(RootDirectory) && !Path.IsPathFullyQualified(RootDirectory))
            throw new InvalidOperationException("Backups:RootDirectory must be an absolute local directory.");
    }
}

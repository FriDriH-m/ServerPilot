namespace ServerPilot.Domain.Backups;

public sealed record BackupReference(Guid Id, long SizeBytes, string Checksum);

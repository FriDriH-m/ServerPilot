namespace ServerPilot.Agent.Api;

public sealed record ClaimedAgentCommand(
    Guid Id,
    Guid ServerInstanceId,
    AgentCommandType Type,
    Guid CorrelationId,
    string DeliveryKind,
    ClaimedAgentServerInstance ServerInstance,
    IReadOnlyList<AgentBackupReference>? BackupTargets = null);

public sealed record AgentBackupReference(Guid Id, long SizeBytes, string Checksum);

public enum AgentCommandType
{
    StartServer = 0,
    StopServer,
    CreateBackup,
    RestoreBackup,
    PruneBackups,
}

public sealed record ClaimedAgentServerInstance(
    string Profile,
    string ExecutablePath,
    string Arguments,
    string WorkingDirectory,
    string ProcessName,
    string? DataDirectory);

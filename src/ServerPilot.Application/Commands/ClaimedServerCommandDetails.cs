namespace ServerPilot.Application.Commands;

public sealed record ClaimedServerCommandDetails(
    ServerCommandDetails Command,
    AgentCommandDeliveryKind DeliveryKind,
    ServerInstanceExecutionDetails ServerInstance,
    IReadOnlyList<ServerPilot.Domain.Backups.BackupReference>? BackupTargets = null);

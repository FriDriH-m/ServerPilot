namespace ServerPilot.Application.Commands;

public sealed record ClaimedServerCommandDetails(
    ServerCommandDetails Command,
    AgentCommandDeliveryKind DeliveryKind,
    ServerInstanceExecutionDetails ServerInstance);

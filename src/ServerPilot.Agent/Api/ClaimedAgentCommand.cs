namespace ServerPilot.Agent.Api;

public sealed record ClaimedAgentCommand(
    Guid Id,
    Guid ServerInstanceId,
    string Type,
    Guid CorrelationId,
    string DeliveryKind);

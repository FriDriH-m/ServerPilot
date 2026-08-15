namespace ServerPilot.Agent.Api;

public sealed record ClaimedAgentCommand(
    Guid Id,
    Guid ServerInstanceId,
    AgentCommandType Type,
    Guid CorrelationId,
    string DeliveryKind,
    ClaimedAgentServerInstance ServerInstance);

public enum AgentCommandType
{
    StartServer = 0,
    StopServer,
}

public sealed record ClaimedAgentServerInstance(
    string Profile,
    string ExecutablePath,
    string Arguments,
    string WorkingDirectory,
    string ProcessName,
    string? DataDirectory);

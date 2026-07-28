namespace ServerPilot.Application.Agents;

public sealed record RegisterAgentResult(
    RegisterAgentStatus Status,
    RegisteredAgent? Agent);

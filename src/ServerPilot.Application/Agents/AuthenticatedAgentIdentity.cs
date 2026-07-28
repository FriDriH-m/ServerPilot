namespace ServerPilot.Application.Agents;

public sealed record AuthenticatedAgentIdentity(Guid AgentId, Guid UserId);

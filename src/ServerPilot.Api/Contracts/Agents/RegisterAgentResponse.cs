namespace ServerPilot.Api.Contracts.Agents;

public sealed record RegisterAgentResponse(
    Guid AgentId,
    string Credential,
    string AuthorizationScheme,
    DateTimeOffset RegisteredAt);

namespace ServerPilot.Application.Agents;

public sealed record RegisteredAgent(
    Guid Id,
    Guid UserId,
    string RawCredential,
    DateTimeOffset RegisteredAt);

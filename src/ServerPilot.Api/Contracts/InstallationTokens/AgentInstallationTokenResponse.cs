namespace ServerPilot.Api.Contracts.InstallationTokens;

public sealed record AgentInstallationTokenResponse(
    Guid Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? UsedAt,
    DateTimeOffset? RevokedAt,
    string State);

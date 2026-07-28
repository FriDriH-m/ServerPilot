namespace ServerPilot.Api.Contracts.InstallationTokens;

public sealed record CreateAgentInstallationTokenResponse(
    Guid Id,
    string Token,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

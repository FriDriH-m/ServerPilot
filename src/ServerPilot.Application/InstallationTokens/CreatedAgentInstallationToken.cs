namespace ServerPilot.Application.InstallationTokens;

public sealed record CreatedAgentInstallationToken(
    Guid Id,
    string RawToken,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

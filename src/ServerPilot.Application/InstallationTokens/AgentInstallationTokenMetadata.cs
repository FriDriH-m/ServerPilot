using ServerPilot.Domain.InstallationTokens;

namespace ServerPilot.Application.InstallationTokens;

public sealed record AgentInstallationTokenMetadata(
    Guid Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? UsedAt,
    DateTimeOffset? RevokedAt,
    AgentInstallationTokenState State);

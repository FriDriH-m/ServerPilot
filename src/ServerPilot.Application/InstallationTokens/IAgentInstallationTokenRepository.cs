using ServerPilot.Domain.InstallationTokens;

namespace ServerPilot.Application.InstallationTokens;

public interface IAgentInstallationTokenRepository
{
    Task<AddAgentInstallationTokenStatus> TryAddAsync(
        AgentInstallationToken installationToken,
        DateTimeOffset now,
        DateTimeOffset metadataRetentionCutoff,
        int maximumActiveTokensPerUser,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AgentInstallationToken>> ListByUserIdAsync(
        Guid userId,
        int skip,
        int limit,
        CancellationToken cancellationToken);

    Task<RevokeAgentInstallationTokenStatus> RevokeOwnedAsync(
        Guid id,
        Guid userId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken);
}

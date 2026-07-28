using ServerPilot.Domain.InstallationTokens;

namespace ServerPilot.Application.InstallationTokens;

public interface IAgentInstallationTokenRepository
{
    Task<bool> TryAddAsync(
        AgentInstallationToken installationToken,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AgentInstallationToken>> ListByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<AgentInstallationToken?> FindOwnedByIdAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

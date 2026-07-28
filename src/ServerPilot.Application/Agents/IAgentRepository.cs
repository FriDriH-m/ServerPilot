using ServerPilot.Domain.Agents;

namespace ServerPilot.Application.Agents;

public interface IAgentRepository
{
    Task<AgentInstallationTokenIdentity?> FindActiveInstallationTokenAsync(
        string installationTokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<RegisterAgentPersistenceStatus> TryRegisterAsync(
        Agent agent,
        Guid installationTokenId,
        string installationTokenHash,
        DateTimeOffset usedAt,
        CancellationToken cancellationToken);

    Task<AuthenticatedAgentIdentity?> FindAuthenticatedByCredentialHashAsync(
        string credentialHash,
        CancellationToken cancellationToken);

    Task<RevokeAgentCredentialStatus> RevokeOwnedCredentialsAsync(
        Guid agentId,
        Guid userId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken);
}

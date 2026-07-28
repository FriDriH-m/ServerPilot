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

    Task RecordHeartbeatAsync(
        Guid agentId,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AgentMetadata>> ListOwnedAsync(
        Guid userId,
        int skip,
        int limit,
        CancellationToken cancellationToken);

    Task<AgentMetadata?> FindOwnedAsync(
        Guid agentId,
        Guid userId,
        CancellationToken cancellationToken);

    Task<RevokeAgentCredentialStatus> RevokeOwnedCredentialsAsync(
        Guid agentId,
        Guid userId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken);
}

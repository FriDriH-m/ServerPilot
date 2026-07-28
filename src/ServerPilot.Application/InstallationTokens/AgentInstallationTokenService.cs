using ServerPilot.Domain.InstallationTokens;

namespace ServerPilot.Application.InstallationTokens;

public sealed class AgentInstallationTokenService(
    IAgentInstallationTokenRepository installationTokens,
    IAgentInstallationTokenGenerator tokenGenerator,
    AgentInstallationTokenOptions options,
    TimeProvider timeProvider)
{
    private const int TokenGenerationAttempts = 3;

    public async Task<CreatedAgentInstallationToken> CreateAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User ID cannot be empty.", nameof(userId));
        }

        DateTimeOffset createdAt = timeProvider.GetUtcNow();
        DateTimeOffset expiresAt = createdAt.AddMinutes(options.LifetimeMinutes);

        for (int attempt = 0; attempt < TokenGenerationAttempts; attempt++)
        {
            GeneratedAgentInstallationToken generatedToken = tokenGenerator.Generate();
            AgentInstallationToken installationToken = AgentInstallationToken.Create(
                Guid.NewGuid(),
                userId,
                generatedToken.TokenHash,
                createdAt,
                expiresAt);

            if (await installationTokens.TryAddAsync(installationToken, cancellationToken))
            {
                return new CreatedAgentInstallationToken(
                    installationToken.Id,
                    generatedToken.RawToken,
                    installationToken.CreatedAt,
                    installationToken.ExpiresAt);
            }
        }

        throw new InvalidOperationException(
            "Unable to generate a unique Agent installation token.");
    }

    public async Task<IReadOnlyList<AgentInstallationTokenMetadata>> ListAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User ID cannot be empty.", nameof(userId));
        }

        IReadOnlyList<AgentInstallationToken> tokens =
            await installationTokens.ListByUserIdAsync(userId, cancellationToken);
        DateTimeOffset now = timeProvider.GetUtcNow();

        return tokens
            .Select(token => new AgentInstallationTokenMetadata(
                token.Id,
                token.CreatedAt,
                token.ExpiresAt,
                token.UsedAt,
                token.RevokedAt,
                token.GetState(now)))
            .ToArray();
    }

    public async Task<RevokeAgentInstallationTokenStatus> RevokeAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            return RevokeAgentInstallationTokenStatus.NotFound;
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User ID cannot be empty.", nameof(userId));
        }

        AgentInstallationToken? token = await installationTokens.FindOwnedByIdAsync(
            id,
            userId,
            cancellationToken);
        if (token is null)
        {
            return RevokeAgentInstallationTokenStatus.NotFound;
        }

        AgentInstallationTokenRevocationResult result = token.TryRevoke(
            timeProvider.GetUtcNow());
        if (result == AgentInstallationTokenRevocationResult.AlreadyUsed)
        {
            return RevokeAgentInstallationTokenStatus.AlreadyUsed;
        }

        if (result == AgentInstallationTokenRevocationResult.Succeeded)
        {
            await installationTokens.SaveChangesAsync(cancellationToken);
        }

        return RevokeAgentInstallationTokenStatus.Succeeded;
    }
}

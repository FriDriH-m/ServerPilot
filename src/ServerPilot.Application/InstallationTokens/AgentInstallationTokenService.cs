using ServerPilot.Domain.InstallationTokens;

namespace ServerPilot.Application.InstallationTokens;

public sealed class AgentInstallationTokenService(
    IAgentInstallationTokenRepository installationTokens,
    IAgentInstallationTokenGenerator tokenGenerator,
    AgentInstallationTokenOptions options,
    TimeProvider timeProvider)
{
    private const int TokenGenerationAttempts = 3;

    public async Task<CreateAgentInstallationTokenResult> CreateAsync(
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

            AddAgentInstallationTokenStatus addStatus =
                await installationTokens.TryAddAsync(
                    installationToken,
                    createdAt,
                    createdAt.AddDays(-options.MetadataRetentionDays),
                    options.MaximumActiveTokensPerUser,
                    cancellationToken);
            if (addStatus == AddAgentInstallationTokenStatus.Succeeded)
            {
                return new CreateAgentInstallationTokenResult(
                    CreateAgentInstallationTokenStatus.Succeeded,
                    new CreatedAgentInstallationToken(
                        installationToken.Id,
                        generatedToken.RawToken,
                        installationToken.CreatedAt,
                        installationToken.ExpiresAt));
            }

            if (addStatus == AddAgentInstallationTokenStatus.ActiveLimitReached)
            {
                return new CreateAgentInstallationTokenResult(
                    CreateAgentInstallationTokenStatus.ActiveLimitReached,
                    null);
            }
        }

        throw new InvalidOperationException(
            "Unable to generate a unique Agent installation token.");
    }

    public async Task<IReadOnlyList<AgentInstallationTokenMetadata>> ListAsync(
        Guid userId,
        int page,
        int limit,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User ID cannot be empty.", nameof(userId));
        }

        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                "Installation token list limit must be between 1 and 100.");
        }

        if (page is < 1 or > 1_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(page),
                "Installation token list page must be between 1 and 1000.");
        }

        IReadOnlyList<AgentInstallationToken> tokens =
            await installationTokens.ListByUserIdAsync(
                userId,
                (page - 1) * limit,
                limit,
                cancellationToken);
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

        return await installationTokens.RevokeOwnedAsync(
            id,
            userId,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }
}

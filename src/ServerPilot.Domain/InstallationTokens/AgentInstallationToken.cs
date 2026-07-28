namespace ServerPilot.Domain.InstallationTokens;

public sealed class AgentInstallationToken
{
    public const int TokenHashLength = 64;

    private AgentInstallationToken()
    {
    }

    private AgentInstallationToken(
        Guid id,
        Guid userId,
        string tokenHash,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Installation token ID cannot be empty.", nameof(id));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User ID cannot be empty.", nameof(userId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        if (tokenHash.Length != TokenHashLength)
        {
            throw new ArgumentException(
                $"Installation token hash must be {TokenHashLength} characters.",
                nameof(tokenHash));
        }

        DateTimeOffset utcCreatedAt = createdAt.ToUniversalTime();
        DateTimeOffset utcExpiresAt = expiresAt.ToUniversalTime();
        if (utcExpiresAt <= utcCreatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expiresAt),
                "Installation token expiration must be later than its creation time.");
        }

        Id = id;
        UserId = userId;
        TokenHash = tokenHash;
        CreatedAt = utcCreatedAt;
        ExpiresAt = utcExpiresAt;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string TokenHash { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? UsedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public static AgentInstallationToken Create(
        Guid id,
        Guid userId,
        string tokenHash,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt) =>
        new(id, userId, tokenHash, createdAt, expiresAt);

    public AgentInstallationTokenState GetState(DateTimeOffset now)
    {
        if (UsedAt.HasValue)
        {
            return AgentInstallationTokenState.Used;
        }

        if (RevokedAt.HasValue)
        {
            return AgentInstallationTokenState.Revoked;
        }

        return now.ToUniversalTime() >= ExpiresAt
            ? AgentInstallationTokenState.Expired
            : AgentInstallationTokenState.Active;
    }

    public AgentInstallationTokenUseResult TryUse(DateTimeOffset usedAt)
    {
        if (UsedAt.HasValue)
        {
            return AgentInstallationTokenUseResult.AlreadyUsed;
        }

        if (RevokedAt.HasValue)
        {
            return AgentInstallationTokenUseResult.Revoked;
        }

        DateTimeOffset utcUsedAt = usedAt.ToUniversalTime();
        if (utcUsedAt >= ExpiresAt)
        {
            return AgentInstallationTokenUseResult.Expired;
        }

        if (utcUsedAt < CreatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(usedAt),
                "Installation token cannot be used before it was created.");
        }

        UsedAt = utcUsedAt;
        return AgentInstallationTokenUseResult.Succeeded;
    }

    public AgentInstallationTokenRevocationResult TryRevoke(DateTimeOffset revokedAt)
    {
        if (UsedAt.HasValue)
        {
            return AgentInstallationTokenRevocationResult.AlreadyUsed;
        }

        if (RevokedAt.HasValue)
        {
            return AgentInstallationTokenRevocationResult.AlreadyRevoked;
        }

        DateTimeOffset utcRevokedAt = revokedAt.ToUniversalTime();
        if (utcRevokedAt < CreatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(revokedAt),
                "Installation token cannot be revoked before it was created.");
        }

        RevokedAt = utcRevokedAt;
        return AgentInstallationTokenRevocationResult.Succeeded;
    }
}

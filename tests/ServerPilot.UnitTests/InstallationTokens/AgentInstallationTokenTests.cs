using ServerPilot.Domain.InstallationTokens;

namespace ServerPilot.UnitTests.InstallationTokens;

public sealed class AgentInstallationTokenTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateRequiresExpirationAfterCreation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateToken(CreatedAt));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateToken(CreatedAt.AddMinutes(-1)));
    }

    [Fact]
    public void CreateRequiresCanonicalLowercaseHexadecimalHash()
    {
        Assert.Throws<ArgumentException>(() => AgentInstallationToken.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new string('G', AgentInstallationToken.TokenHashLength),
            CreatedAt,
            CreatedAt.AddMinutes(15)));
    }

    [Fact]
    public void NewTokenIsActiveBeforeExpirationAndExpiredAtBoundary()
    {
        AgentInstallationToken token = CreateToken(CreatedAt.AddMinutes(15));

        Assert.Equal(
            AgentInstallationTokenState.Active,
            token.GetState(CreatedAt.AddMinutes(14)));
        Assert.Equal(
            AgentInstallationTokenState.Expired,
            token.GetState(token.ExpiresAt));
    }

    [Fact]
    public void ActiveTokenCanBeUsedExactlyOnce()
    {
        AgentInstallationToken token = CreateToken(CreatedAt.AddMinutes(15));
        DateTimeOffset usedAt = CreatedAt.AddMinutes(1);

        AgentInstallationTokenUseResult firstResult = token.TryUse(usedAt);
        AgentInstallationTokenUseResult secondResult = token.TryUse(usedAt.AddMinutes(1));

        Assert.Equal(AgentInstallationTokenUseResult.Succeeded, firstResult);
        Assert.Equal(AgentInstallationTokenUseResult.AlreadyUsed, secondResult);
        Assert.Equal(usedAt, token.UsedAt);
        Assert.Equal(AgentInstallationTokenState.Used, token.GetState(usedAt));
    }

    [Fact]
    public void ExpiredTokenCannotBeUsed()
    {
        AgentInstallationToken token = CreateToken(CreatedAt.AddMinutes(15));

        AgentInstallationTokenUseResult result = token.TryUse(token.ExpiresAt);

        Assert.Equal(AgentInstallationTokenUseResult.Expired, result);
        Assert.Null(token.UsedAt);
        Assert.Equal(AgentInstallationTokenState.Expired, token.GetState(token.ExpiresAt));
    }

    [Fact]
    public void RevokedTokenCannotBeUsed()
    {
        AgentInstallationToken token = CreateToken(CreatedAt.AddMinutes(15));
        DateTimeOffset revokedAt = CreatedAt.AddMinutes(1);

        AgentInstallationTokenRevocationResult revokeResult = token.TryRevoke(revokedAt);
        AgentInstallationTokenUseResult useResult = token.TryUse(revokedAt.AddMinutes(1));

        Assert.Equal(AgentInstallationTokenRevocationResult.Succeeded, revokeResult);
        Assert.Equal(AgentInstallationTokenUseResult.Revoked, useResult);
        Assert.Equal(revokedAt, token.RevokedAt);
        Assert.Null(token.UsedAt);
        Assert.Equal(AgentInstallationTokenState.Revoked, token.GetState(revokedAt));
    }

    [Fact]
    public void UsedTokenCannotBeRevoked()
    {
        AgentInstallationToken token = CreateToken(CreatedAt.AddMinutes(15));
        Assert.Equal(
            AgentInstallationTokenUseResult.Succeeded,
            token.TryUse(CreatedAt.AddMinutes(1)));

        AgentInstallationTokenRevocationResult result = token.TryRevoke(
            CreatedAt.AddMinutes(2));

        Assert.Equal(AgentInstallationTokenRevocationResult.AlreadyUsed, result);
        Assert.Null(token.RevokedAt);
    }

    [Fact]
    public void RepeatedRevocationDoesNotChangeTimestamp()
    {
        AgentInstallationToken token = CreateToken(CreatedAt.AddMinutes(15));
        DateTimeOffset firstRevokedAt = CreatedAt.AddMinutes(1);

        AgentInstallationTokenRevocationResult firstResult = token.TryRevoke(firstRevokedAt);
        AgentInstallationTokenRevocationResult secondResult = token.TryRevoke(
            CreatedAt.AddMinutes(2));

        Assert.Equal(AgentInstallationTokenRevocationResult.Succeeded, firstResult);
        Assert.Equal(AgentInstallationTokenRevocationResult.AlreadyRevoked, secondResult);
        Assert.Equal(firstRevokedAt, token.RevokedAt);
    }

    private static AgentInstallationToken CreateToken(DateTimeOffset expiresAt) =>
        AgentInstallationToken.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new string('a', AgentInstallationToken.TokenHashLength),
            CreatedAt,
            expiresAt);
}

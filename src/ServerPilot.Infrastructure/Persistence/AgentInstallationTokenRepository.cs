using System.Buffers.Binary;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using ServerPilot.Application.InstallationTokens;
using ServerPilot.Domain.InstallationTokens;
using ServerPilot.Infrastructure.Persistence.Configurations;

namespace ServerPilot.Infrastructure.Persistence;

internal sealed class AgentInstallationTokenRepository(ServerPilotDbContext dbContext)
    : IAgentInstallationTokenRepository
{
    public async Task<AddAgentInstallationTokenStatus> TryAddAsync(
        AgentInstallationToken installationToken,
        DateTimeOffset now,
        DateTimeOffset metadataRetentionCutoff,
        int maximumActiveTokensPerUser,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);
        long advisoryLockKey = BinaryPrimitives.ReadInt64LittleEndian(
            installationToken.UserId.ToByteArray());
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({advisoryLockKey})",
            cancellationToken);

        await dbContext.AgentInstallationTokens
            .Where(token =>
                token.UserId == installationToken.UserId &&
                token.CreatedAt < metadataRetentionCutoff &&
                (token.UsedAt != null ||
                    token.RevokedAt != null ||
                    token.ExpiresAt <= now))
            .ExecuteDeleteAsync(cancellationToken);

        int activeTokenCount = await dbContext.AgentInstallationTokens.CountAsync(
            token =>
                token.UserId == installationToken.UserId &&
                token.UsedAt == null &&
                token.RevokedAt == null &&
                token.ExpiresAt > now,
            cancellationToken);
        if (activeTokenCount >= maximumActiveTokensPerUser)
        {
            await transaction.CommitAsync(cancellationToken);
            return AddAgentInstallationTokenStatus.ActiveLimitReached;
        }

        dbContext.AgentInstallationTokens.Add(installationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return AddAgentInstallationTokenStatus.Succeeded;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: AgentInstallationTokenConfiguration.TokenHashUniqueIndexName,
            })
        {
            dbContext.Entry(installationToken).State = EntityState.Detached;
            await transaction.RollbackAsync(cancellationToken);
            return AddAgentInstallationTokenStatus.TokenHashCollision;
        }
    }

    public async Task<IReadOnlyList<AgentInstallationToken>> ListByUserIdAsync(
        Guid userId,
        int skip,
        int limit,
        CancellationToken cancellationToken) =>
        await dbContext.AgentInstallationTokens
            .AsNoTracking()
            .Where(token => token.UserId == userId)
            .OrderByDescending(token => token.CreatedAt)
            .ThenByDescending(token => token.Id)
            .Skip(skip)
            .Take(limit)
            .ToArrayAsync(cancellationToken);

    public async Task<RevokeAgentInstallationTokenStatus> RevokeOwnedAsync(
        Guid id,
        Guid userId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken)
    {
        DateTimeOffset utcRevokedAt = revokedAt.ToUniversalTime();
        int updatedRows = await dbContext.AgentInstallationTokens
            .Where(token =>
                token.Id == id &&
                token.UserId == userId &&
                token.UsedAt == null &&
                token.RevokedAt == null &&
                token.CreatedAt <= utcRevokedAt)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    token => token.RevokedAt,
                    utcRevokedAt),
                cancellationToken);
        if (updatedRows == 1)
        {
            return RevokeAgentInstallationTokenStatus.Succeeded;
        }

        var tokenState = await dbContext.AgentInstallationTokens
            .AsNoTracking()
            .Where(token => token.Id == id && token.UserId == userId)
            .Select(token => new { token.UsedAt, token.RevokedAt, token.CreatedAt })
            .SingleOrDefaultAsync(cancellationToken);
        if (tokenState is null)
        {
            return RevokeAgentInstallationTokenStatus.NotFound;
        }

        if (tokenState.UsedAt.HasValue)
        {
            return RevokeAgentInstallationTokenStatus.AlreadyUsed;
        }

        if (tokenState.RevokedAt.HasValue)
        {
            return RevokeAgentInstallationTokenStatus.AlreadyRevoked;
        }

        throw new InvalidOperationException(
            "Installation token cannot be revoked before its creation time.");
    }
}

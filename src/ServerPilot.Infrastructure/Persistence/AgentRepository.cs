using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using ServerPilot.Application.Agents;
using ServerPilot.Infrastructure.Persistence.Configurations;
using AgentEntity = ServerPilot.Domain.Agents.Agent;

namespace ServerPilot.Infrastructure.Persistence;

internal sealed class AgentRepository(ServerPilotDbContext dbContext) : IAgentRepository
{
    public Task<AgentInstallationTokenIdentity?> FindActiveInstallationTokenAsync(
        string installationTokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        DateTimeOffset utcNow = now.ToUniversalTime();
        return dbContext.AgentInstallationTokens
            .AsNoTracking()
            .Where(token =>
                token.TokenHash == installationTokenHash &&
                token.CreatedAt <= utcNow &&
                token.ExpiresAt > utcNow &&
                token.UsedAt == null &&
                token.RevokedAt == null)
            .Select(token => new AgentInstallationTokenIdentity(token.Id, token.UserId))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<RegisterAgentPersistenceStatus> TryRegisterAsync(
        AgentEntity agent,
        Guid installationTokenId,
        string installationTokenHash,
        DateTimeOffset usedAt,
        CancellationToken cancellationToken)
    {
        DateTimeOffset utcUsedAt = usedAt.ToUniversalTime();
        await using IDbContextTransaction transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        int consumedTokens = await dbContext.AgentInstallationTokens
            .Where(token =>
                token.Id == installationTokenId &&
                token.TokenHash == installationTokenHash &&
                token.UserId == agent.UserId &&
                token.CreatedAt <= utcUsedAt &&
                token.ExpiresAt > utcUsedAt &&
                token.UsedAt == null &&
                token.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.UsedAt, utcUsedAt),
                cancellationToken);
        if (consumedTokens == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return RegisterAgentPersistenceStatus.InstallationTokenInactive;
        }

        dbContext.Agents.Add(agent);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return RegisterAgentPersistenceStatus.Succeeded;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: AgentConfiguration.CredentialHashUniqueIndexName,
            })
        {
            dbContext.Entry(agent).State = EntityState.Detached;
            await transaction.RollbackAsync(cancellationToken);
            return RegisterAgentPersistenceStatus.CredentialHashCollision;
        }
    }

    public Task<AuthenticatedAgentIdentity?> FindAuthenticatedByCredentialHashAsync(
        string credentialHash,
        CancellationToken cancellationToken) =>
        dbContext.Agents
            .AsNoTracking()
            .Where(agent =>
                agent.CredentialHash == credentialHash &&
                agent.CredentialRevokedAt == null)
            .Select(agent => new AuthenticatedAgentIdentity(agent.Id, agent.UserId))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task RecordHeartbeatAsync(
        Guid agentId,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken)
    {
        DateTimeOffset utcReceivedAt = receivedAt.ToUniversalTime();
        await dbContext.Agents
            .Where(agent =>
                agent.Id == agentId &&
                agent.RegisteredAt <= utcReceivedAt &&
                (agent.LastSeenAt == null || agent.LastSeenAt < utcReceivedAt))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    agent => agent.LastSeenAt,
                    utcReceivedAt),
                cancellationToken);
    }

    public async Task<IReadOnlyList<AgentMetadata>> ListOwnedAsync(
        Guid userId,
        int skip,
        int limit,
        CancellationToken cancellationToken) =>
        await ProjectMetadata(
                dbContext.Agents
                    .AsNoTracking()
                    .Where(agent => agent.UserId == userId)
                    .OrderByDescending(agent => agent.RegisteredAt)
                    .ThenByDescending(agent => agent.Id))
            .Skip(skip)
            .Take(limit)
            .ToArrayAsync(cancellationToken);

    public Task<AgentMetadata?> FindOwnedAsync(
        Guid agentId,
        Guid userId,
        CancellationToken cancellationToken) =>
        ProjectMetadata(
                dbContext.Agents
                    .AsNoTracking()
                    .Where(agent => agent.Id == agentId && agent.UserId == userId))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<RevokeAgentCredentialStatus> RevokeOwnedCredentialsAsync(
        Guid agentId,
        Guid userId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken)
    {
        DateTimeOffset utcRevokedAt = revokedAt.ToUniversalTime();
        int updatedRows = await dbContext.Agents
            .Where(agent =>
                agent.Id == agentId &&
                agent.UserId == userId &&
                agent.CredentialRevokedAt == null &&
                agent.RegisteredAt <= utcRevokedAt)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    agent => agent.CredentialRevokedAt,
                    utcRevokedAt),
                cancellationToken);
        if (updatedRows == 1)
        {
            return RevokeAgentCredentialStatus.Succeeded;
        }

        AgentRevocationState? currentState = await dbContext.Agents
            .AsNoTracking()
            .Where(agent => agent.Id == agentId && agent.UserId == userId)
            .Select(agent => new AgentRevocationState(
                agent.RegisteredAt,
                agent.CredentialRevokedAt))
            .SingleOrDefaultAsync(cancellationToken);
        if (currentState is null)
        {
            return RevokeAgentCredentialStatus.NotFound;
        }

        if (currentState.CredentialRevokedAt.HasValue)
        {
            return RevokeAgentCredentialStatus.AlreadyRevoked;
        }

        throw new InvalidOperationException(
            "Agent credentials cannot be revoked before registration.");
    }

    private sealed record AgentRevocationState(
        DateTimeOffset RegisteredAt,
        DateTimeOffset? CredentialRevokedAt);

    private static IQueryable<AgentMetadata> ProjectMetadata(
        IQueryable<AgentEntity> query) =>
        query.Select(agent => new AgentMetadata(
            agent.Id,
            agent.Name,
            agent.MachineName,
            agent.OperatingSystem,
            agent.Version,
            agent.RegisteredAt,
            agent.LastSeenAt));
}

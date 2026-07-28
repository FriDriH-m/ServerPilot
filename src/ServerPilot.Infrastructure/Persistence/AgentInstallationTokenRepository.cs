using Microsoft.EntityFrameworkCore;
using Npgsql;
using ServerPilot.Application.InstallationTokens;
using ServerPilot.Domain.InstallationTokens;
using ServerPilot.Infrastructure.Persistence.Configurations;

namespace ServerPilot.Infrastructure.Persistence;

internal sealed class AgentInstallationTokenRepository(ServerPilotDbContext dbContext)
    : IAgentInstallationTokenRepository
{
    public async Task<bool> TryAddAsync(
        AgentInstallationToken installationToken,
        CancellationToken cancellationToken)
    {
        dbContext.AgentInstallationTokens.Add(installationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: AgentInstallationTokenConfiguration.TokenHashUniqueIndexName,
            })
        {
            dbContext.Entry(installationToken).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<IReadOnlyList<AgentInstallationToken>> ListByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        await dbContext.AgentInstallationTokens
            .AsNoTracking()
            .Where(token => token.UserId == userId)
            .OrderByDescending(token => token.CreatedAt)
            .ToArrayAsync(cancellationToken);

    public Task<AgentInstallationToken?> FindOwnedByIdAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken) =>
        dbContext.AgentInstallationTokens.SingleOrDefaultAsync(
            token => token.Id == id && token.UserId == userId,
            cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}

using Microsoft.EntityFrameworkCore;
using Npgsql;
using ServerPilot.Application.Authentication;
using ServerPilot.Domain.Users;
using ServerPilot.Infrastructure.Persistence.Configurations;

namespace ServerPilot.Infrastructure.Persistence;

internal sealed class UserRepository(ServerPilotDbContext dbContext) : IUserRepository
{
    public Task<User?> FindByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken) =>
        dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(
                user => user.NormalizedEmail == normalizedEmail,
                cancellationToken);

    public async Task<bool> TryAddAsync(User user, CancellationToken cancellationToken)
    {
        dbContext.Users.Add(user);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: UserConfiguration.NormalizedEmailUniqueIndexName,
            })
        {
            dbContext.Entry(user).State = EntityState.Detached;
            return false;
        }
    }

    public Task UpdatePasswordHashAsync(
        Guid userId,
        string currentPasswordHash,
        string newPasswordHash,
        CancellationToken cancellationToken) =>
        dbContext.Users
            .Where(user =>
                user.Id == userId &&
                user.PasswordHash == currentPasswordHash)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    user => user.PasswordHash,
                    newPasswordHash),
                cancellationToken);
}

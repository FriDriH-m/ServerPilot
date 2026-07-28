using ServerPilot.Domain.Users;

namespace ServerPilot.Application.Authentication;

public interface IUserRepository
{
    Task<User?> FindByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken);

    Task<bool> TryAddAsync(User user, CancellationToken cancellationToken);

    Task UpdatePasswordHashAsync(
        Guid userId,
        string currentPasswordHash,
        string newPasswordHash,
        CancellationToken cancellationToken);
}

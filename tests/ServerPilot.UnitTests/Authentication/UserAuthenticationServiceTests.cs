using ServerPilot.Application.Authentication;
using ServerPilot.Domain.Users;

namespace ServerPilot.UnitTests.Authentication;

public sealed class UserAuthenticationServiceTests
{
    [Fact]
    public async Task LoginPersistsUpdatedHashWhenVerificationRequestsRehash()
    {
        User user = User.Create(
            Guid.NewGuid(),
            "user@example.com",
            "USER@EXAMPLE.COM",
            "old-password-hash",
            new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero));
        RecordingUserRepository users = new(user);
        UserAuthenticationService service = new(
            users,
            new RehashingPasswordService(),
            new StubAccessTokenIssuer(),
            TimeProvider.System);

        AuthenticationSession? session = await service.LoginAsync(
            user.Email,
            "correct password",
            CancellationToken.None);

        Assert.NotNull(session);
        Assert.Equal(user.Id, users.UpdatedUserId);
        Assert.Equal("old-password-hash", users.PreviousPasswordHash);
        Assert.Equal("new-password-hash", users.NewPasswordHash);
    }

    private sealed class RecordingUserRepository(User user) : IUserRepository
    {
        public Guid? UpdatedUserId { get; private set; }
        public string? PreviousPasswordHash { get; private set; }
        public string? NewPasswordHash { get; private set; }

        public Task<User?> FindByNormalizedEmailAsync(
            string normalizedEmail,
            CancellationToken cancellationToken) => Task.FromResult<User?>(user);

        public Task<bool> TryAddAsync(User newUser, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task UpdatePasswordHashAsync(
            Guid userId,
            string currentPasswordHash,
            string newPasswordHash,
            CancellationToken cancellationToken)
        {
            UpdatedUserId = userId;
            PreviousPasswordHash = currentPasswordHash;
            NewPasswordHash = newPasswordHash;
            return Task.CompletedTask;
        }
    }

    private sealed class RehashingPasswordService : IPasswordHashingService
    {
        public string HashPassword(Guid userId, string password) => "new-password-hash";

        public PasswordVerificationOutcome VerifyPassword(
            Guid? userId,
            string? passwordHash,
            string providedPassword) => new(true, true);
    }

    private sealed class StubAccessTokenIssuer : IAccessTokenIssuer
    {
        public AccessToken Issue(User user, DateTimeOffset issuedAt) =>
            new("access-token", issuedAt.AddMinutes(30));
    }
}

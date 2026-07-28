using ServerPilot.Domain.Users;

namespace ServerPilot.Application.Authentication;

public sealed class UserAuthenticationService(
    IUserRepository users,
    IPasswordHashingService passwordHashing,
    IAccessTokenIssuer accessTokenIssuer,
    TimeProvider timeProvider)
{
    public async Task<RegisterUserResult> RegisterAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        string canonicalEmail = EmailNormalizer.Canonicalize(email);
        string normalizedEmail = EmailNormalizer.Normalize(canonicalEmail);

        User? existingUser = await users.FindByNormalizedEmailAsync(
            normalizedEmail,
            cancellationToken);
        if (existingUser is not null)
        {
            return new RegisterUserResult(RegisterUserStatus.DuplicateEmail, null);
        }

        Guid userId = Guid.NewGuid();
        string passwordHash = passwordHashing.HashPassword(userId, password);
        DateTimeOffset createdAt = timeProvider.GetUtcNow();
        User user = User.Create(
            userId,
            canonicalEmail,
            normalizedEmail,
            passwordHash,
            createdAt);

        if (!await users.TryAddAsync(user, cancellationToken))
        {
            return new RegisterUserResult(RegisterUserStatus.DuplicateEmail, null);
        }

        AuthenticationSession session = CreateSession(user, createdAt);
        return new RegisterUserResult(RegisterUserStatus.Succeeded, session);
    }

    public async Task<AuthenticationSession?> LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        string normalizedEmail = EmailNormalizer.Normalize(email);
        User? user = await users.FindByNormalizedEmailAsync(
            normalizedEmail,
            cancellationToken);

        PasswordVerificationOutcome passwordVerification = passwordHashing.VerifyPassword(
            user?.Id,
            user?.PasswordHash,
            password);
        if (user is null || !passwordVerification.IsValid)
        {
            return null;
        }

        if (passwordVerification.RequiresRehash)
        {
            string updatedPasswordHash = passwordHashing.HashPassword(user.Id, password);
            await users.UpdatePasswordHashAsync(
                user.Id,
                user.PasswordHash,
                updatedPasswordHash,
                cancellationToken);
        }

        return CreateSession(user, timeProvider.GetUtcNow());
    }

    private AuthenticationSession CreateSession(User user, DateTimeOffset issuedAt) =>
        new(user.Id, user.Email, accessTokenIssuer.Issue(user, issuedAt));
}

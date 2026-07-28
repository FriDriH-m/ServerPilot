using Microsoft.AspNetCore.Identity;
using ServerPilot.Application.Authentication;

namespace ServerPilot.Infrastructure.Authentication;

internal sealed class AspNetCorePasswordHashingService : IPasswordHashingService
{
    private const string DummyPassword = "ServerPilot dummy verification password";

    private readonly PasswordHasher<PasswordHashSubject> passwordHasher = new();
    private readonly string dummyPasswordHash;

    public AspNetCorePasswordHashingService()
    {
        dummyPasswordHash = passwordHasher.HashPassword(
            new PasswordHashSubject(Guid.Empty),
            DummyPassword);
    }

    public string HashPassword(Guid userId, string password)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User ID cannot be empty.", nameof(userId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        return passwordHasher.HashPassword(new PasswordHashSubject(userId), password);
    }

    public bool VerifyPassword(Guid? userId, string? passwordHash, string providedPassword)
    {
        ArgumentNullException.ThrowIfNull(providedPassword);

        PasswordHashSubject verificationUser = new(userId ?? Guid.Empty);
        string verificationHash = passwordHash ?? dummyPasswordHash;
        PasswordVerificationResult result = passwordHasher.VerifyHashedPassword(
            verificationUser,
            verificationHash,
            providedPassword);

        return userId.HasValue &&
            passwordHash is not null &&
            result is PasswordVerificationResult.Success or
                PasswordVerificationResult.SuccessRehashNeeded;
    }

    private sealed record PasswordHashSubject(Guid UserId);
}

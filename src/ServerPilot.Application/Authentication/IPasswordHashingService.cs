namespace ServerPilot.Application.Authentication;

public interface IPasswordHashingService
{
    string HashPassword(Guid userId, string password);

    PasswordVerificationOutcome VerifyPassword(
        Guid? userId,
        string? passwordHash,
        string providedPassword);
}

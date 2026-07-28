namespace ServerPilot.Application.Authentication;

public interface IPasswordHashingService
{
    string HashPassword(Guid userId, string password);

    bool VerifyPassword(Guid? userId, string? passwordHash, string providedPassword);
}

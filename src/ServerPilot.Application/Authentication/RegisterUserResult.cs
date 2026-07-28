namespace ServerPilot.Application.Authentication;

public enum RegisterUserStatus
{
    Succeeded,
    DuplicateEmail,
}

public sealed record RegisterUserResult(
    RegisterUserStatus Status,
    AuthenticationSession? Session);

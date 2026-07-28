namespace ServerPilot.Application.Authentication;

public sealed record AuthenticationSession(
    Guid UserId,
    string Email,
    AccessToken AccessToken);

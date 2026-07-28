namespace ServerPilot.Api.Contracts.Authentication;

public sealed record AuthenticationResponse(
    Guid UserId,
    string Email,
    string AccessToken,
    DateTimeOffset ExpiresAt);

namespace ServerPilot.Application.Authentication;

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

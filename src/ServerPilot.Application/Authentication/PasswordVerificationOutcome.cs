namespace ServerPilot.Application.Authentication;

public readonly record struct PasswordVerificationOutcome(
    bool IsValid,
    bool RequiresRehash);

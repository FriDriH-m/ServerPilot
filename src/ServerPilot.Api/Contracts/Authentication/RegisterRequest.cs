using System.ComponentModel.DataAnnotations;

namespace ServerPilot.Api.Contracts.Authentication;

public sealed class RegisterRequest
{
    [Required, EmailAddress, StringLength(254)]
    public string? Email { get; init; }

    [Required, StringLength(128, MinimumLength = 12)]
    public string? Password { get; init; }
}

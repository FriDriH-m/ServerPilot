using System.ComponentModel.DataAnnotations;

namespace ServerPilot.Api.Contracts.Authentication;

public sealed class LoginRequest
{
    [Required, EmailAddress, StringLength(254)]
    public string? Email { get; init; }

    [Required, StringLength(128)]
    public string? Password { get; init; }
}

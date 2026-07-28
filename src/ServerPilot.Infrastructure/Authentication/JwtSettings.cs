using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace ServerPilot.Infrastructure.Authentication;

public sealed class JwtSettings
{
    public const string SectionName = "Authentication:Jwt";
    public const string UnsafeExampleSigningKey = "replace-with-at-least-32-random-bytes";

    public string Issuer { get; init; } = string.Empty;

    public string Audience { get; init; } = string.Empty;

    public string SigningKey { get; init; } = string.Empty;

    public int AccessTokenLifetimeMinutes { get; init; } = 30;

    internal TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(AccessTokenLifetimeMinutes);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Issuer))
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:Issuer' is required.");
        }

        if (string.IsNullOrWhiteSpace(Audience))
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:Audience' is required.");
        }

        if (Encoding.UTF8.GetByteCount(SigningKey) < 32)
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:SigningKey' must contain at least 32 UTF-8 bytes.");
        }

        if (string.Equals(SigningKey, UnsafeExampleSigningKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:SigningKey' must not use the public example value.");
        }

        if (AccessTokenLifetimeMinutes is < 1 or > 1440)
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:AccessTokenLifetimeMinutes' must be between 1 and 1440.");
        }
    }

    internal SymmetricSecurityKey CreateSigningKey() =>
        new(Encoding.UTF8.GetBytes(SigningKey));
}

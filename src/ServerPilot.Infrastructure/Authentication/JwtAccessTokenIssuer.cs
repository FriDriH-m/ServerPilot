using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using ServerPilot.Application.Authentication;
using ServerPilot.Domain.Users;

namespace ServerPilot.Infrastructure.Authentication;

internal sealed class JwtAccessTokenIssuer(JwtSettings settings) : IAccessTokenIssuer
{
    private readonly JwtSecurityTokenHandler tokenHandler = new();

    public AccessToken Issue(User user, DateTimeOffset issuedAt)
    {
        ArgumentNullException.ThrowIfNull(user);

        DateTimeOffset expiresAt = issuedAt.Add(settings.AccessTokenLifetime);
        Claim[] claims =
        [
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        ];

        JwtSecurityToken token = new(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims: claims,
            notBefore: issuedAt.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(
                settings.CreateSigningKey(),
                SecurityAlgorithms.HmacSha256));

        return new AccessToken(tokenHandler.WriteToken(token), expiresAt);
    }
}

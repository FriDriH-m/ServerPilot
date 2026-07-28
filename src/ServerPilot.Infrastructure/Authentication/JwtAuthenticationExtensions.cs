using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace ServerPilot.Infrastructure.Authentication;

internal static class JwtAuthenticationExtensions
{
    public static IServiceCollection AddServerPilotJwtAuthentication(
        this IServiceCollection services,
        JwtSettings settings)
    {
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = settings.Issuer,
                    ValidateAudience = true,
                    ValidAudience = settings.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = settings.CreateSigningKey(),
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        string? subject = context.Principal?.FindFirst(
                            JwtRegisteredClaimNames.Sub)?.Value;
                        if (!Guid.TryParse(subject, out _))
                        {
                            context.Fail("The access token subject is missing or invalid.");
                        }

                        return Task.CompletedTask;
                    },
                };
            });

        return services;
    }
}

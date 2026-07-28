using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServerPilot.Application.Authentication;
using ServerPilot.Application.InstallationTokens;
using ServerPilot.Infrastructure.Authentication;
using ServerPilot.Infrastructure.InstallationTokens;
using ServerPilot.Infrastructure.Persistence;

namespace ServerPilot.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string postgreSqlConnectionString,
        JwtSettings jwtSettings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(postgreSqlConnectionString);
        ArgumentNullException.ThrowIfNull(jwtSettings);
        jwtSettings.Validate();

        services.AddDbContext<ServerPilotDbContext>(
            options => options.UseNpgsql(postgreSqlConnectionString));
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IAgentInstallationTokenRepository, AgentInstallationTokenRepository>();
        services.AddSingleton<IAgentInstallationTokenGenerator,
            CryptographicAgentInstallationTokenGenerator>();
        services.AddSingleton<IPasswordHashingService, AspNetCorePasswordHashingService>();
        services.AddScoped<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddSingleton(jwtSettings);
        services.AddServerPilotJwtAuthentication(jwtSettings);

        return services;
    }
}

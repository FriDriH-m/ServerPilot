using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServerPilot.Application.Agents;
using ServerPilot.Application.Authentication;
using ServerPilot.Application.Commands;
using ServerPilot.Application.InstallationTokens;
using ServerPilot.Application.ServerInstances;
using ServerPilot.Infrastructure.Agents;
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
        services.AddScoped<IAgentRepository, AgentRepository>();
        services.AddScoped<IAgentInstallationTokenRepository, AgentInstallationTokenRepository>();
        services.AddScoped<IServerInstanceRepository, ServerInstanceRepository>();
        services.AddScoped<IServerCommandRepository, ServerCommandRepository>();
        services.AddSingleton<IAgentCredentialGenerator,
            CryptographicAgentCredentialGenerator>();
        services.AddSingleton<IAgentInstallationTokenGenerator,
            CryptographicAgentInstallationTokenGenerator>();
        services.AddSingleton<IPasswordHashingService, AspNetCorePasswordHashingService>();
        services.AddScoped<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddSingleton(jwtSettings);
        services.AddServerPilotJwtAuthentication(jwtSettings);
        services.AddServerPilotAgentAuthentication();

        return services;
    }
}

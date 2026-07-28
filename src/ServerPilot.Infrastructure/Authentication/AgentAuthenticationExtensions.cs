using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace ServerPilot.Infrastructure.Authentication;

internal static class AgentAuthenticationExtensions
{
    public static IServiceCollection AddServerPilotAgentAuthentication(
        this IServiceCollection services)
    {
        services
            .AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, AgentCredentialAuthenticationHandler>(
                AgentAuthenticationDefaults.AuthenticationScheme,
                _ => { });

        return services;
    }
}

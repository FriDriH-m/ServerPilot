using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerPilot.Application.Agents;

namespace ServerPilot.Infrastructure.Authentication;

internal sealed class AgentCredentialAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AgentCredentialAuthenticationService authentication)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private static readonly Action<ILogger, Exception?> LogAgentAuthenticationFailed =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1200, nameof(LogAgentAuthenticationFailed)),
            "Agent authentication failed");

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string authorization = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            return AuthenticateResult.NoResult();
        }

        if (!AuthenticationHeaderValue.TryParse(authorization, out var header) ||
            !string.Equals(
                header.Scheme,
                AgentAuthenticationDefaults.AuthenticationScheme,
                StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        if (string.IsNullOrWhiteSpace(header.Parameter))
        {
            LogAgentAuthenticationFailed(Logger, null);
            return AuthenticateResult.Fail("Invalid Agent credentials.");
        }

        AuthenticatedAgentIdentity? agent = await authentication.AuthenticateAsync(
            header.Parameter,
            Context.RequestAborted);
        if (agent is null)
        {
            LogAgentAuthenticationFailed(Logger, null);
            return AuthenticateResult.Fail("Invalid Agent credentials.");
        }

        Claim[] claims =
        [
            new Claim(
                AgentAuthenticationDefaults.AgentIdClaimType,
                agent.AgentId.ToString()),
            new Claim(
                AgentAuthenticationDefaults.OwnerUserIdClaimType,
                agent.UserId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, agent.AgentId.ToString()),
        ];
        ClaimsIdentity identity = new(
            claims,
            AgentAuthenticationDefaults.AuthenticationScheme);
        ClaimsPrincipal principal = new(identity);
        AuthenticationTicket ticket = new(
            principal,
            AgentAuthenticationDefaults.AuthenticationScheme);
        return AuthenticateResult.Success(ticket);
    }

    protected override async Task HandleChallengeAsync(
        AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate =
            AgentAuthenticationDefaults.AuthenticationScheme;
        await base.HandleChallengeAsync(properties);
    }
}

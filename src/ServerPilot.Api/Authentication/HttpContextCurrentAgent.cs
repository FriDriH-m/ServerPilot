using ServerPilot.Application.Agents;
using ServerPilot.Infrastructure.Authentication;

namespace ServerPilot.Api.Authentication;

internal sealed class HttpContextCurrentAgent(IHttpContextAccessor httpContextAccessor)
    : ICurrentAgent
{
    public Guid? AgentId
    {
        get
        {
            string? claim = httpContextAccessor.HttpContext?.User.FindFirst(
                AgentAuthenticationDefaults.AgentIdClaimType)?.Value;
            return Guid.TryParse(claim, out Guid agentId) ? agentId : null;
        }
    }
}

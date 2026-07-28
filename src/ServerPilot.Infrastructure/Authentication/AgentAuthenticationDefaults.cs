namespace ServerPilot.Infrastructure.Authentication;

public static class AgentAuthenticationDefaults
{
    public const string AuthenticationScheme = "Agent";
    public const string AgentIdClaimType = "serverpilot:agent_id";
    public const string OwnerUserIdClaimType = "serverpilot:owner_user_id";
}

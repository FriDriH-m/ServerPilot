namespace ServerPilot.Agent.Credentials;

public sealed class AgentCredential
{
    public const string ExpectedAuthorizationScheme = "Agent";

    private AgentCredential(Guid agentId, string value, string authorizationScheme)
    {
        AgentId = agentId;
        Value = value;
        AuthorizationScheme = authorizationScheme;
    }

    public Guid AgentId { get; }

    public string Value { get; }

    public string AuthorizationScheme { get; }

    public static AgentCredential Create(
        Guid agentId,
        string? value,
        string? authorizationScheme)
    {
        if (agentId == Guid.Empty)
        {
            throw new InvalidOperationException("Agent credential has an invalid Agent ID.");
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Agent credential is missing.");
        }

        if (authorizationScheme is not ExpectedAuthorizationScheme)
        {
            throw new InvalidOperationException("Agent credential has an unsupported authorization scheme.");
        }

        return new AgentCredential(agentId, value, authorizationScheme);
    }

    public override string ToString() => $"Agent credential for {AgentId}";
}

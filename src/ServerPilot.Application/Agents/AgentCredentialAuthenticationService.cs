namespace ServerPilot.Application.Agents;

public sealed class AgentCredentialAuthenticationService(
    IAgentRepository agents,
    IAgentCredentialGenerator credentialGenerator)
{
    public async Task<AuthenticatedAgentIdentity?> AuthenticateAsync(
        string rawCredential,
        CancellationToken cancellationToken)
    {
        if (!AgentCredentialFormat.IsValid(rawCredential))
        {
            return null;
        }

        string credentialHash = credentialGenerator.ComputeHash(rawCredential);
        return await agents.FindAuthenticatedByCredentialHashAsync(
            credentialHash,
            cancellationToken);
    }
}

namespace ServerPilot.Application.Agents;

public interface IAgentCredentialGenerator
{
    GeneratedAgentCredential Generate();

    string ComputeHash(string rawCredential);
}

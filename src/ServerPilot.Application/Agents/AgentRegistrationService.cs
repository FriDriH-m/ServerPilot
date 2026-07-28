using ServerPilot.Application.InstallationTokens;
using ServerPilot.Domain.Agents;

namespace ServerPilot.Application.Agents;

public sealed class AgentRegistrationService(
    IAgentRepository agents,
    IAgentInstallationTokenGenerator installationTokenGenerator,
    IAgentCredentialGenerator credentialGenerator,
    TimeProvider timeProvider)
{
    private const int CredentialGenerationAttempts = 3;
    private const int MaximumInstallationTokenLength = 256;

    public async Task<RegisterAgentResult> RegisterAsync(
        string installationToken,
        string name,
        string machineName,
        string operatingSystem,
        string version,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationToken);
        if (installationToken.Length > MaximumInstallationTokenLength)
        {
            return new RegisterAgentResult(RegisterAgentStatus.InvalidInstallationToken, null);
        }

        DateTimeOffset registeredAt = timeProvider.GetUtcNow();
        string installationTokenHash = installationTokenGenerator.ComputeHash(
            installationToken);
        AgentInstallationTokenIdentity? installationIdentity =
            await agents.FindActiveInstallationTokenAsync(
                installationTokenHash,
                registeredAt,
                cancellationToken);
        if (installationIdentity is null)
        {
            return new RegisterAgentResult(RegisterAgentStatus.InvalidInstallationToken, null);
        }

        for (int attempt = 0; attempt < CredentialGenerationAttempts; attempt++)
        {
            GeneratedAgentCredential generatedCredential = credentialGenerator.Generate();
            Agent agent = Agent.Create(
                Guid.NewGuid(),
                installationIdentity.UserId,
                name,
                machineName,
                operatingSystem,
                version,
                generatedCredential.CredentialHash,
                registeredAt);
            RegisterAgentPersistenceStatus status = await agents.TryRegisterAsync(
                agent,
                installationIdentity.TokenId,
                installationTokenHash,
                registeredAt,
                cancellationToken);

            if (status == RegisterAgentPersistenceStatus.Succeeded)
            {
                return new RegisterAgentResult(
                    RegisterAgentStatus.Succeeded,
                    new RegisteredAgent(
                        agent.Id,
                        agent.UserId,
                        generatedCredential.RawCredential,
                        agent.RegisteredAt));
            }

            if (status == RegisterAgentPersistenceStatus.InstallationTokenInactive)
            {
                return new RegisterAgentResult(
                    RegisterAgentStatus.InvalidInstallationToken,
                    null);
            }
        }

        throw new InvalidOperationException(
            "Could not generate a unique Agent credential after multiple attempts.");
    }
}

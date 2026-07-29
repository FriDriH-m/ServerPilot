using ServerPilot.Agent.Credentials;

namespace ServerPilot.Agent.Runtime;

public sealed class AgentRuntime
{
    private AgentCredential? credential;

    public void Initialize(AgentCredential initializedCredential)
    {
        ArgumentNullException.ThrowIfNull(initializedCredential);

        if (Interlocked.CompareExchange(ref credential, initializedCredential, null) is not null)
        {
            throw new InvalidOperationException("Agent runtime credentials have already been initialized.");
        }
    }

    public AgentCredential GetCredential() =>
        Volatile.Read(ref credential) ??
        throw new InvalidOperationException("Agent runtime credentials have not been initialized.");
}

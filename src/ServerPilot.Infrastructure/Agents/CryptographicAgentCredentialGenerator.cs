using System.Security.Cryptography;
using System.Text;
using ServerPilot.Application.Agents;

namespace ServerPilot.Infrastructure.Agents;

internal sealed class CryptographicAgentCredentialGenerator : IAgentCredentialGenerator
{
    private const int RandomByteCount = 32;

    public GeneratedAgentCredential Generate()
    {
        string randomValue = Convert.ToHexString(
            RandomNumberGenerator.GetBytes(RandomByteCount));
        string rawCredential = $"{AgentCredentialFormat.Prefix}{randomValue}";

        return new GeneratedAgentCredential(
            rawCredential,
            ComputeHash(rawCredential));
    }

    public string ComputeHash(string rawCredential)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawCredential);

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawCredential));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

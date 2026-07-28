using System.Security.Cryptography;
using System.Text;
using ServerPilot.Application.InstallationTokens;

namespace ServerPilot.Infrastructure.InstallationTokens;

internal sealed class CryptographicAgentInstallationTokenGenerator
    : IAgentInstallationTokenGenerator
{
    private const int RandomByteCount = 32;
    private const string TokenPrefix = "spit_";

    public GeneratedAgentInstallationToken Generate()
    {
        string randomValue = Convert.ToHexString(
            RandomNumberGenerator.GetBytes(RandomByteCount));
        string rawToken = $"{TokenPrefix}{randomValue}";

        return new GeneratedAgentInstallationToken(rawToken, ComputeHash(rawToken));
    }

    public string ComputeHash(string rawToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawToken);

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

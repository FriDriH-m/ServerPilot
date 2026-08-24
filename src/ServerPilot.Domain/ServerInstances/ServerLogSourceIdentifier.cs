using System.Security.Cryptography;
using System.Text;

namespace ServerPilot.Domain.ServerInstances;

public static class ServerLogSourceIdentifier
{
    public const int Length = 64;

    public static string? Create(
        ServerInstanceProfile profile,
        string? dataDirectory)
    {
        if (profile != ServerInstanceProfile.ProjectZomboid ||
            string.IsNullOrWhiteSpace(dataDirectory))
        {
            return null;
        }

        string normalizedPath = dataDirectory
            .Trim()
            .Replace('/', '\\')
            .TrimEnd('\\')
            .ToUpperInvariant();
        byte[] source = Encoding.UTF8.GetBytes($"{(int)profile}\n{normalizedPath}");
        return Convert.ToHexString(SHA256.HashData(source));
    }

    public static bool IsValid(string? value) =>
        value is { Length: Length } && value.All(Uri.IsHexDigit);
}

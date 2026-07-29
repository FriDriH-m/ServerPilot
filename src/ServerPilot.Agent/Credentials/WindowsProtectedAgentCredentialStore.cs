using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ServerPilot.Agent.Credentials;

public sealed class WindowsProtectedAgentCredentialStore : IAgentCredentialStore
{
    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("ServerPilot.Agent.Credential.v1");
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly string credentialPath;

    public WindowsProtectedAgentCredentialStore()
        : this(GetDefaultCredentialPath())
    {
    }

    internal WindowsProtectedAgentCredentialStore(string credentialPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "ServerPilot Agent credential storage requires Windows DPAPI.");
        }

        this.credentialPath = credentialPath;
    }

    public async Task<AgentCredential?> ReadAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "ServerPilot Agent credential storage requires Windows DPAPI.");
        }

        if (!File.Exists(credentialPath))
        {
            return null;
        }

        byte[] protectedPayload = await File.ReadAllBytesAsync(credentialPath, cancellationToken);
        byte[] payload = ProtectedData.Unprotect(
            protectedPayload,
            Entropy,
            DataProtectionScope.CurrentUser);
        try
        {
            PersistedAgentCredential? storedCredential = JsonSerializer.Deserialize<PersistedAgentCredential>(
                payload,
                SerializerOptions);
            if (storedCredential is null)
            {
                throw new InvalidOperationException("Stored Agent credential is invalid.");
            }

            return AgentCredential.Create(
                storedCredential.AgentId,
                storedCredential.Value,
                storedCredential.AuthorizationScheme);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Stored Agent credential is invalid.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    public async Task SaveAsync(AgentCredential credential, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credential);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "ServerPilot Agent credential storage requires Windows DPAPI.");
        }

        string? directoryPath = Path.GetDirectoryName(credentialPath);
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new InvalidOperationException("Agent credential storage path is invalid.");
        }

        Directory.CreateDirectory(directoryPath);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
            new PersistedAgentCredential(
                credential.AgentId,
                credential.Value,
                credential.AuthorizationScheme),
            SerializerOptions);
        try
        {
            byte[] protectedPayload = ProtectedData.Protect(
                payload,
                Entropy,
                DataProtectionScope.CurrentUser);
            try
            {
                string temporaryPath = Path.Combine(
                    directoryPath,
                    $".{Path.GetFileName(credentialPath)}.{Guid.NewGuid():N}.tmp");
                try
                {
                    await File.WriteAllBytesAsync(temporaryPath, protectedPayload, cancellationToken);
                    File.Move(temporaryPath, credentialPath, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(protectedPayload);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    private static string GetDefaultCredentialPath()
    {
        string localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            throw new InvalidOperationException(
                "The current user's local application-data directory is unavailable.");
        }

        return Path.Combine(localApplicationData, "ServerPilot", "agent-credential.dat");
    }

    private sealed record PersistedAgentCredential(
        Guid AgentId,
        string Value,
        string AuthorizationScheme);
}

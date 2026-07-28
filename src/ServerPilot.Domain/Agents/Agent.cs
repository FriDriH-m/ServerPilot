namespace ServerPilot.Domain.Agents;

public sealed class Agent
{
    public const int CredentialHashLength = 64;
    public const int MaximumNameLength = 100;
    public const int MaximumMachineNameLength = 255;
    public const int MaximumOperatingSystemLength = 255;
    public const int MaximumVersionLength = 64;

    private Agent()
    {
    }

    private Agent(
        Guid id,
        Guid userId,
        string name,
        string machineName,
        string operatingSystem,
        string version,
        string credentialHash,
        DateTimeOffset registeredAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Agent ID cannot be empty.", nameof(id));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User ID cannot be empty.", nameof(userId));
        }

        Id = id;
        UserId = userId;
        Name = NormalizeRequiredValue(name, MaximumNameLength, nameof(name));
        MachineName = NormalizeRequiredValue(
            machineName,
            MaximumMachineNameLength,
            nameof(machineName));
        OperatingSystem = NormalizeRequiredValue(
            operatingSystem,
            MaximumOperatingSystemLength,
            nameof(operatingSystem));
        Version = NormalizeRequiredValue(version, MaximumVersionLength, nameof(version));
        CredentialHash = ValidateCredentialHash(credentialHash);
        RegisteredAt = registeredAt.ToUniversalTime();
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string Name { get; private set; } = null!;

    public string MachineName { get; private set; } = null!;

    public string OperatingSystem { get; private set; } = null!;

    public string Version { get; private set; } = null!;

    public string CredentialHash { get; private set; } = null!;

    public DateTimeOffset RegisteredAt { get; private set; }

    public DateTimeOffset? LastSeenAt { get; private set; }

    public DateTimeOffset? CredentialRevokedAt { get; private set; }

    public static Agent Create(
        Guid id,
        Guid userId,
        string name,
        string machineName,
        string operatingSystem,
        string version,
        string credentialHash,
        DateTimeOffset registeredAt) =>
        new(
            id,
            userId,
            name,
            machineName,
            operatingSystem,
            version,
            credentialHash,
            registeredAt);

    public bool RecordHeartbeat(DateTimeOffset receivedAt)
    {
        DateTimeOffset utcReceivedAt = receivedAt.ToUniversalTime();
        if (utcReceivedAt < RegisteredAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(receivedAt),
                "Agent heartbeat cannot be recorded before registration.");
        }

        if (LastSeenAt.HasValue && utcReceivedAt <= LastSeenAt.Value)
        {
            return false;
        }

        LastSeenAt = utcReceivedAt;
        return true;
    }

    public AgentCredentialRevocationResult RevokeCredentials(DateTimeOffset revokedAt)
    {
        if (CredentialRevokedAt.HasValue)
        {
            return AgentCredentialRevocationResult.AlreadyRevoked;
        }

        DateTimeOffset utcRevokedAt = revokedAt.ToUniversalTime();
        if (utcRevokedAt < RegisteredAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(revokedAt),
                "Agent credentials cannot be revoked before registration.");
        }

        CredentialRevokedAt = utcRevokedAt;
        return AgentCredentialRevocationResult.Succeeded;
    }

    private static string NormalizeRequiredValue(
        string value,
        int maximumLength,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        string normalizedValue = value.Trim();
        if (normalizedValue.Length > maximumLength)
        {
            throw new ArgumentException(
                $"{parameterName} cannot exceed {maximumLength} characters.",
                parameterName);
        }

        return normalizedValue;
    }

    private static string ValidateCredentialHash(string credentialHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialHash);
        if (credentialHash.Length != CredentialHashLength ||
            credentialHash.Any(character =>
                character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException(
                $"Agent credential hash must contain exactly {CredentialHashLength} " +
                "lowercase hexadecimal characters.",
                nameof(credentialHash));
        }

        return credentialHash;
    }
}

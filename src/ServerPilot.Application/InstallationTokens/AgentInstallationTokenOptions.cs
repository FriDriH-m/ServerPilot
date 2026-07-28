namespace ServerPilot.Application.InstallationTokens;

public sealed class AgentInstallationTokenOptions
{
    public const string SectionName = "AgentInstallationTokens";
    public const int DefaultLifetimeMinutes = 15;
    public const int MaximumLifetimeMinutes = 1_440;
    public const int DefaultMaximumActiveTokensPerUser = 10;
    public const int MaximumAllowedActiveTokensPerUser = 100;
    public const int DefaultMetadataRetentionDays = 90;
    public const int MaximumMetadataRetentionDays = 3_650;

    public int LifetimeMinutes { get; init; } = DefaultLifetimeMinutes;

    public int MaximumActiveTokensPerUser { get; init; } =
        DefaultMaximumActiveTokensPerUser;

    public int MetadataRetentionDays { get; init; } = DefaultMetadataRetentionDays;

    public void Validate()
    {
        if (LifetimeMinutes is < 1 or > MaximumLifetimeMinutes)
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(LifetimeMinutes)} must be between 1 and " +
                $"{MaximumLifetimeMinutes}.");
        }

        if (MaximumActiveTokensPerUser is < 1 or > MaximumAllowedActiveTokensPerUser)
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(MaximumActiveTokensPerUser)} must be between 1 and " +
                $"{MaximumAllowedActiveTokensPerUser}.");
        }

        if (MetadataRetentionDays is < 1 or > MaximumMetadataRetentionDays)
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(MetadataRetentionDays)} must be between 1 and " +
                $"{MaximumMetadataRetentionDays}.");
        }
    }
}

namespace ServerPilot.Application.InstallationTokens;

public sealed class AgentInstallationTokenOptions
{
    public const string SectionName = "AgentInstallationTokens";
    public const int DefaultLifetimeMinutes = 15;
    public const int MaximumLifetimeMinutes = 1_440;

    public int LifetimeMinutes { get; init; } = DefaultLifetimeMinutes;

    public void Validate()
    {
        if (LifetimeMinutes is < 1 or > MaximumLifetimeMinutes)
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(LifetimeMinutes)} must be between 1 and " +
                $"{MaximumLifetimeMinutes}.");
        }
    }
}

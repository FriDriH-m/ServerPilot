namespace ServerPilot.Application.Agents;

public sealed class AgentAvailabilityOptions
{
    public const string SectionName = "AgentAvailability";
    public const int DefaultOfflineThresholdSeconds = 30;
    public const int MaximumOfflineThresholdSeconds = 86_400;

    public int OfflineThresholdSeconds { get; init; } =
        DefaultOfflineThresholdSeconds;

    public TimeSpan OfflineThreshold =>
        TimeSpan.FromSeconds(OfflineThresholdSeconds);

    public void Validate()
    {
        if (OfflineThresholdSeconds is < 1 or > MaximumOfflineThresholdSeconds)
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(OfflineThresholdSeconds)} must be between 1 and " +
                $"{MaximumOfflineThresholdSeconds}.");
        }
    }
}

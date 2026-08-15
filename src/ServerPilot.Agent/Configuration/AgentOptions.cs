namespace ServerPilot.Agent.Configuration;

public sealed class AgentOptions
{
    public const string SectionName = "Agent";
    private const int MinimumIntervalSeconds = 1;
    private const int MaximumIntervalSeconds = 86_400;

    public string? ApiBaseUrl { get; init; }

    public string? Name { get; init; }

    public string? InstallationToken { get; init; }

    public int HeartbeatIntervalSeconds { get; init; } = 10;

    public int CommandPollingIntervalSeconds { get; init; } = 5;

    public int ProcessReconciliationIntervalSeconds { get; init; } = 10;

    public TimeSpan HeartbeatInterval => TimeSpan.FromSeconds(HeartbeatIntervalSeconds);

    public TimeSpan CommandPollingInterval => TimeSpan.FromSeconds(CommandPollingIntervalSeconds);

    public TimeSpan ProcessReconciliationInterval =>
        TimeSpan.FromSeconds(ProcessReconciliationIntervalSeconds);

    public void Validate()
    {
        Uri apiBaseUri = GetApiBaseUri();
        if (apiBaseUri.Scheme == Uri.UriSchemeHttp && !apiBaseUri.IsLoopback)
        {
            throw new InvalidOperationException(
                "Agent:ApiBaseUrl must use HTTPS unless it targets a loopback address.");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidOperationException("Agent:Name is required.");
        }

        string normalizedName = Name.Trim();
        if (normalizedName.Length > 100 ||
            normalizedName.Any(char.IsControl))
        {
            throw new InvalidOperationException(
                "Agent:Name must not contain control characters or exceed 100 characters.");
        }

        ValidateInterval(HeartbeatIntervalSeconds, nameof(HeartbeatIntervalSeconds));
        ValidateInterval(CommandPollingIntervalSeconds, nameof(CommandPollingIntervalSeconds));
        ValidateInterval(
            ProcessReconciliationIntervalSeconds,
            nameof(ProcessReconciliationIntervalSeconds));
    }

    public Uri GetApiBaseUri()
    {
        if (string.IsNullOrWhiteSpace(ApiBaseUrl) ||
            !Uri.TryCreate(ApiBaseUrl, UriKind.Absolute, out Uri? apiBaseUri) ||
            (apiBaseUri.Scheme != Uri.UriSchemeHttp && apiBaseUri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(apiBaseUri.Query) ||
            !string.IsNullOrEmpty(apiBaseUri.Fragment))
        {
            throw new InvalidOperationException(
                "Agent:ApiBaseUrl must be an absolute HTTP or HTTPS URL without a query or fragment.");
        }

        return apiBaseUri.AbsolutePath.EndsWith('/')
            ? apiBaseUri
            : new UriBuilder(apiBaseUri) { Path = $"{apiBaseUri.AbsolutePath}/" }.Uri;
    }

    public string GetInstallationToken()
    {
        if (string.IsNullOrWhiteSpace(InstallationToken))
        {
            throw new InvalidOperationException(
                "Agent:InstallationToken is required when no stored Agent credential exists.");
        }

        return InstallationToken.Trim();
    }

    private static void ValidateInterval(int intervalSeconds, string propertyName)
    {
        if (intervalSeconds is < MinimumIntervalSeconds or > MaximumIntervalSeconds)
        {
            throw new InvalidOperationException(
                $"Agent:{propertyName} must be between {MinimumIntervalSeconds} and {MaximumIntervalSeconds} seconds.");
        }
    }
}

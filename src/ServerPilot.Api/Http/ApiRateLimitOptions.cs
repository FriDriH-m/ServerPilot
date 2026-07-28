namespace ServerPilot.Api.Http;

public sealed class ApiRateLimitOptions
{
    public const string SectionName = "RateLimiting";

    public int AuthenticationPermitLimit { get; init; } = 10;

    public int UserPermitLimit { get; init; } = 30;

    public int WindowSeconds { get; init; } = 60;

    public void Validate()
    {
        if (AuthenticationPermitLimit is < 1 or > 1_000)
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(AuthenticationPermitLimit)} must be between 1 and 1000.");
        }

        if (UserPermitLimit is < 1 or > 10_000)
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(UserPermitLimit)} must be between 1 and 10000.");
        }

        if (WindowSeconds is < 1 or > 3_600)
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(WindowSeconds)} must be between 1 and 3600.");
        }
    }
}

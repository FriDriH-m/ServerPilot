using Microsoft.Extensions.Primitives;

namespace ServerPilot.Api.Http;

public sealed class CorrelationIdMiddleware(
    RequestDelegate next,
    ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ProblemDetailsExtensionName = "correlationId";

    private const int MaximumCorrelationIdLength = 64;

    public async Task InvokeAsync(HttpContext context)
    {
        string correlationId = GetCorrelationId(context.Request);
        context.TraceIdentifier = correlationId;

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using IDisposable? scope = logger.BeginScope(
            new Dictionary<string, object?>
            {
                ["CorrelationId"] = correlationId,
            });

        await next(context);
    }

    private static string GetCorrelationId(HttpRequest request)
    {
        if (request.Headers.TryGetValue(HeaderName, out StringValues headerValues) &&
            headerValues.Count == 1)
        {
            string? candidate = headerValues[0];
            if (IsValid(candidate))
            {
                return candidate!;
            }
        }

        return Guid.NewGuid().ToString("N");
    }

    private static bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaximumCorrelationIdLength)
        {
            return false;
        }

        foreach (char character in value)
        {
            if (!IsAllowedCharacter(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAllowedCharacter(char character) =>
        character is (>= 'a' and <= 'z')
            or (>= 'A' and <= 'Z')
            or (>= '0' and <= '9')
            or '-'
            or '_'
            or '.';
}

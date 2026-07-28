using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace ServerPilot.Api.Http;

internal static class ApiRateLimitingExtensions
{
    public static IServiceCollection AddServerPilotRateLimiting(
        this IServiceCollection services,
        ApiRateLimitOptions settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = static async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(
                    MetadataName.RetryAfter,
                    out TimeSpan retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(
                        retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                await Results.Problem(
                    statusCode: StatusCodes.Status429TooManyRequests,
                    title: "Too Many Requests",
                    detail: "Too many requests. Try again later.",
                    instance: context.HttpContext.Request.Path,
                    extensions: new Dictionary<string, object?>
                    {
                        [CorrelationIdMiddleware.ProblemDetailsExtensionName] =
                            context.HttpContext.TraceIdentifier,
                    }).ExecuteAsync(context.HttpContext);
            };

            options.AddPolicy(
                ApiRateLimitPolicyNames.Authentication,
                context => CreateFixedWindowPartition(
                    GetClientPartitionKey(context),
                    settings.AuthenticationPermitLimit,
                    settings.WindowSeconds));
            options.AddPolicy(
                ApiRateLimitPolicyNames.AuthenticatedUser,
                context => CreateFixedWindowPartition(
                    GetUserPartitionKey(context),
                    settings.UserPermitLimit,
                    settings.WindowSeconds));
        });

        return services;
    }

    private static RateLimitPartition<string> CreateFixedWindowPartition(
        string partitionKey,
        int permitLimit,
        int windowSeconds) =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = permitLimit,
                QueueLimit = 0,
                Window = TimeSpan.FromSeconds(windowSeconds),
            });

    private static string GetClientPartitionKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown-client";

    private static string GetUserPartitionKey(HttpContext context) =>
        context.User.FindFirstValue(JwtRegisteredClaimNames.Sub) ??
        GetClientPartitionKey(context);
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ServerPilot.Infrastructure.Persistence;

namespace ServerPilot.Api.Health;

internal sealed class PostgreSqlReadinessHealthCheck(ServerPilotDbContext dbContext)
    : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await dbContext.Database.CanConnectAsync(cancellationToken))
            {
                return HealthCheckResult.Unhealthy("PostgreSQL is unavailable.");
            }

            string[] pendingMigrations = (await dbContext.Database
                .GetPendingMigrationsAsync(cancellationToken))
                .ToArray();
            return pendingMigrations.Length == 0
                ? HealthCheckResult.Healthy("PostgreSQL is ready and the schema is current.")
                : HealthCheckResult.Unhealthy("PostgreSQL has pending migrations.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy(
                "PostgreSQL readiness check failed.",
                exception);
        }
    }
}

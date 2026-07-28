using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using ServerPilot.Infrastructure.Persistence;

namespace ServerPilot.IntegrationTests.Infrastructure;

public sealed class ServerPilotApiFactory(
    string postgreSqlConnectionString,
    TestLogProvider? logProvider = null,
    TimeProvider? timeProvider = null)
    : WebApplicationFactory<Program>
{
    public const string JwtIssuer = "ServerPilot.IntegrationTests";
    public const string JwtAudience = "ServerPilot.IntegrationTests.Client";
    public const string JwtSigningKey =
        "serverpilot-integration-tests-signing-key-2026";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:PostgreSql", postgreSqlConnectionString);
        builder.UseSetting("Authentication:Jwt:Issuer", JwtIssuer);
        builder.UseSetting("Authentication:Jwt:Audience", JwtAudience);
        builder.UseSetting("Authentication:Jwt:SigningKey", JwtSigningKey);
        builder.UseSetting("Authentication:Jwt:AccessTokenLifetimeMinutes", "30");
        builder.UseSetting("AgentInstallationTokens:LifetimeMinutes", "15");
        builder.UseSetting("AgentAvailability:OfflineThresholdSeconds", "30");
        if (timeProvider is not null)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(timeProvider);
            });
        }

        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            if (logProvider is not null)
            {
                logging.AddProvider(logProvider);
            }
        });
    }

    public async Task ResetDatabaseAsync(CancellationToken cancellationToken = default)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        ServerPilotDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();

        await dbContext.Database.EnsureDeletedAsync(cancellationToken);
        await dbContext.Database.MigrateAsync(cancellationToken);
    }

    public async Task DeleteDatabaseAsync(CancellationToken cancellationToken = default)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        ServerPilotDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();
        await dbContext.Database.EnsureDeletedAsync(cancellationToken);
    }
}

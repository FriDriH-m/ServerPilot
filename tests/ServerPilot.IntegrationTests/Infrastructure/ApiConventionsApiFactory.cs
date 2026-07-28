using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ServerPilot.IntegrationTests.Infrastructure;

public sealed class ApiConventionsApiFactory(TestLogProvider logProvider)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting(
            "ConnectionStrings:PostgreSql",
            "Host=localhost;Port=1;Database=unused;Username=unused;Password=unused");
        builder.UseSetting(
            "Authentication:Jwt:Issuer",
            ServerPilotApiFactory.JwtIssuer);
        builder.UseSetting(
            "Authentication:Jwt:Audience",
            ServerPilotApiFactory.JwtAudience);
        builder.UseSetting(
            "Authentication:Jwt:SigningKey",
            ServerPilotApiFactory.JwtSigningKey);
        builder.ConfigureServices(services =>
            services.AddControllers().AddApplicationPart(typeof(ApiConventionsController).Assembly));
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(logProvider);
        });
    }
}

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using ServerPilot.Api.Authentication;
using ServerPilot.Api.Diagnostics;
using ServerPilot.Api.Health;
using ServerPilot.Api.Http;
using ServerPilot.Application.Agents;
using ServerPilot.Application.Authentication;
using ServerPilot.Application.Commands;
using ServerPilot.Application.InstallationTokens;
using ServerPilot.Application.ServerInstances;
using ServerPilot.Infrastructure;
using ServerPilot.Infrastructure.Authentication;

var builder = WebApplication.CreateBuilder(args);

string? postgreSqlConnectionString = builder.Configuration.GetConnectionString("PostgreSql");
if (string.IsNullOrWhiteSpace(postgreSqlConnectionString))
{
    throw new InvalidOperationException("Connection string 'PostgreSql' is required.");
}

JwtSettings jwtSettings = builder.Configuration
    .GetRequiredSection(JwtSettings.SectionName)
    .Get<JwtSettings>() ??
    throw new InvalidOperationException(
        $"Configuration section '{JwtSettings.SectionName}' is required.");

AgentInstallationTokenOptions installationTokenOptions = builder.Configuration
    .GetSection(AgentInstallationTokenOptions.SectionName)
    .Get<AgentInstallationTokenOptions>() ?? new AgentInstallationTokenOptions();
installationTokenOptions.Validate();

AgentAvailabilityOptions agentAvailabilityOptions = builder.Configuration
    .GetSection(AgentAvailabilityOptions.SectionName)
    .Get<AgentAvailabilityOptions>() ?? new AgentAvailabilityOptions();
agentAvailabilityOptions.Validate();

ApiRateLimitOptions rateLimitOptions = builder.Configuration
    .GetSection(ApiRateLimitOptions.SectionName)
    .Get<ApiRateLimitOptions>() ?? new ApiRateLimitOptions();
rateLimitOptions.Validate();

builder.Services.AddInfrastructure(postgreSqlConnectionString, jwtSettings);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<AgentRegistrationService>();
builder.Services.AddScoped<AgentCredentialAuthenticationService>();
builder.Services.AddScoped<AgentHeartbeatService>();
builder.Services.AddScoped<AgentQueryService>();
builder.Services.AddScoped<AgentManagementService>();
builder.Services.AddScoped<ServerInstanceService>();
builder.Services.AddScoped<AgentServerInstanceService>();
builder.Services.AddScoped<ServerCommandService>();
builder.Services.AddScoped<AgentCommandService>();
builder.Services.AddScoped<UserAuthenticationService>();
builder.Services.AddSingleton(installationTokenOptions);
builder.Services.AddSingleton(agentAvailabilityOptions);
builder.Services.AddScoped<AgentInstallationTokenService>();
builder.Services.AddServerPilotRateLimiting(rateLimitOptions);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentAgent, HttpContextCurrentAgent>();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
builder.Services.AddControllers();
builder.Services.AddHostedService<ApiLifetimeLogger>();
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Instance ??= context.HttpContext.Request.Path.Value;
        context.ProblemDetails.Extensions[CorrelationIdMiddleware.ProblemDetailsExtensionName] =
            context.HttpContext.TraceIdentifier;
    };
});
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(
        AgentAuthorizationPolicyNames.Agent,
        policy =>
        {
            policy.AddAuthenticationSchemes(
                AgentAuthenticationDefaults.AuthenticationScheme);
            policy.RequireAuthenticatedUser();
            policy.RequireClaim(AgentAuthenticationDefaults.AgentIdClaimType);
        });
});
builder.Services.AddHealthChecks()
    .AddCheck<PostgreSqlReadinessHealthCheck>(
        "postgresql",
        tags: ["ready"]);

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    SuppressDiagnosticsCallback = static _ => false,
});
app.UseStatusCodePages();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();
HealthCheckOptions readinessOptions = new()
{
    Predicate = registration => registration.Tags.Contains("ready"),
};
app.MapHealthChecks("/health", readinessOptions);
app.MapHealthChecks("/health/ready", readinessOptions);
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = static _ => false,
});

app.Run();

public partial class Program;

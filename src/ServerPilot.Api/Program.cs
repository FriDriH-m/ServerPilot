using Microsoft.AspNetCore.Diagnostics;
using ServerPilot.Api.Authentication;
using ServerPilot.Api.Http;
using ServerPilot.Application.Authentication;
using ServerPilot.Application.InstallationTokens;
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

builder.Services.AddInfrastructure(postgreSqlConnectionString, jwtSettings);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<UserAuthenticationService>();
builder.Services.AddSingleton(installationTokenOptions);
builder.Services.AddScoped<AgentInstallationTokenService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
builder.Services.AddControllers();
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Instance ??= context.HttpContext.Request.Path.Value;
        context.ProblemDetails.Extensions[CorrelationIdMiddleware.ProblemDetailsExtensionName] =
            context.HttpContext.TraceIdentifier;
    };
});
builder.Services.AddAuthorization();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    SuppressDiagnosticsCallback = static _ => false,
});
app.UseStatusCodePages();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

public partial class Program;

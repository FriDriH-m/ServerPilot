using Microsoft.AspNetCore.Diagnostics;
using ServerPilot.Api.Http;
using ServerPilot.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

string? postgreSqlConnectionString = builder.Configuration.GetConnectionString("PostgreSql");
if (string.IsNullOrWhiteSpace(postgreSqlConnectionString))
{
    throw new InvalidOperationException("Connection string 'PostgreSql' is required.");
}

builder.Services.AddInfrastructure(postgreSqlConnectionString);
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
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

public partial class Program;

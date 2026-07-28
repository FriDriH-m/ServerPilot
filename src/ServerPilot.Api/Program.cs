using ServerPilot.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

string? postgreSqlConnectionString = builder.Configuration.GetConnectionString("PostgreSql");
if (string.IsNullOrWhiteSpace(postgreSqlConnectionString))
{
    throw new InvalidOperationException("Connection string 'PostgreSql' is required.");
}

builder.Services.AddInfrastructure(postgreSqlConnectionString);
builder.Services.AddControllers();
builder.Services.AddAuthorization();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

public partial class Program;

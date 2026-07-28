using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ServerPilot.Infrastructure.Persistence;

public sealed class ServerPilotDbContextDesignTimeFactory
    : IDesignTimeDbContextFactory<ServerPilotDbContext>
{
    public ServerPilotDbContext CreateDbContext(string[] args)
    {
        string? connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__PostgreSql");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Environment variable 'ConnectionStrings__PostgreSql' is required for EF Core tools.");
        }

        DbContextOptions<ServerPilotDbContext> options =
            new DbContextOptionsBuilder<ServerPilotDbContext>()
                .UseNpgsql(connectionString)
                .Options;

        return new ServerPilotDbContext(options);
    }
}

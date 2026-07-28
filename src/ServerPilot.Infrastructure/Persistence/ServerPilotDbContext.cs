using Microsoft.EntityFrameworkCore;
using ServerPilot.Domain.InstallationTokens;
using ServerPilot.Domain.Users;

namespace ServerPilot.Infrastructure.Persistence;

public sealed class ServerPilotDbContext(DbContextOptions<ServerPilotDbContext> options)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<AgentInstallationToken> AgentInstallationTokens =>
        Set<AgentInstallationToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ServerPilotDbContext).Assembly);
    }
}

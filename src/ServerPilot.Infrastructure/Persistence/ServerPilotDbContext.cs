using Microsoft.EntityFrameworkCore;
using ServerPilot.Domain.Agents;
using ServerPilot.Domain.InstallationTokens;
using ServerPilot.Domain.ServerInstances;
using ServerPilot.Domain.Users;

namespace ServerPilot.Infrastructure.Persistence;

public sealed class ServerPilotDbContext(DbContextOptions<ServerPilotDbContext> options)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<AgentInstallationToken> AgentInstallationTokens =>
        Set<AgentInstallationToken>();

    public DbSet<Agent> Agents => Set<Agent>();

    public DbSet<ServerInstance> ServerInstances => Set<ServerInstance>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ServerPilotDbContext).Assembly);
    }
}

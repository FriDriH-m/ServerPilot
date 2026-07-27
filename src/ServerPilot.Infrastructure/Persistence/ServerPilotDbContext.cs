using Microsoft.EntityFrameworkCore;

namespace ServerPilot.Infrastructure.Persistence;

public sealed class ServerPilotDbContext(DbContextOptions<ServerPilotDbContext> options)
    : DbContext(options);

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServerPilot.Infrastructure.Persistence;

namespace ServerPilot.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string postgreSqlConnectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(postgreSqlConnectionString);

        services.AddDbContext<ServerPilotDbContext>(
            options => options.UseNpgsql(postgreSqlConnectionString));

        return services;
    }
}

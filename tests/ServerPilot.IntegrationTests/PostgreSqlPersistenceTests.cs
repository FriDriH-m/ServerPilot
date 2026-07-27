using Microsoft.EntityFrameworkCore;
using ServerPilot.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace ServerPilot.IntegrationTests;

public sealed class PostgreSqlPersistenceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgreSql = new PostgreSqlBuilder("postgres:18.4-alpine").Build();

    public async Task InitializeAsync()
    {
        await postgreSql.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await postgreSql.DisposeAsync();
    }

    [Fact]
    public async Task DbContextConnectsToPostgreSql()
    {
        DbContextOptions<ServerPilotDbContext> options =
            new DbContextOptionsBuilder<ServerPilotDbContext>()
                .UseNpgsql(postgreSql.GetConnectionString())
                .Options;

        await using var dbContext = new ServerPilotDbContext(options);

        Assert.True(await dbContext.Database.CanConnectAsync(CancellationToken.None));
    }
}

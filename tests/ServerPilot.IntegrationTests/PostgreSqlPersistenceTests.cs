using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServerPilot.Infrastructure.Persistence;
using ServerPilot.IntegrationTests.Infrastructure;

namespace ServerPilot.IntegrationTests;

[Collection(PostgreSqlTestGroup.Name)]
public sealed class PostgreSqlPersistenceTests : IAsyncLifetime, IDisposable
{
    private readonly ServerPilotApiFactory factory;

    public PostgreSqlPersistenceTests(PostgreSqlDatabaseFixture database)
    {
        factory = new ServerPilotApiFactory(database.ConnectionString);
    }

    public Task InitializeAsync() => factory.ResetDatabaseAsync(CancellationToken.None);

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        factory.Dispose();
    }

    [Fact]
    public async Task ApiDbContextConnectsAndHasAppliedMigration()
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ServerPilotDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();

        Assert.True(await dbContext.Database.CanConnectAsync(CancellationToken.None));

        string[] appliedMigrations =
            [.. await dbContext.Database.GetAppliedMigrationsAsync(CancellationToken.None)];

        Assert.Contains(
            appliedMigrations,
            migration => migration.EndsWith("_InitialInfrastructure", StringComparison.Ordinal));
    }
}

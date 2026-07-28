using Testcontainers.PostgreSql;

namespace ServerPilot.IntegrationTests.Infrastructure;

public sealed class PostgreSqlDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgreSql =
        new PostgreSqlBuilder("postgres:18.4-alpine")
            .WithDatabase("serverpilot_tests")
            .Build();

    public string ConnectionString => postgreSql.GetConnectionString();

    public Task InitializeAsync() => postgreSql.StartAsync(CancellationToken.None);

    public async Task DisposeAsync() => await postgreSql.DisposeAsync();
}

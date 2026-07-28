namespace ServerPilot.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class PostgreSqlTestGroup : ICollectionFixture<PostgreSqlDatabaseFixture>
{
    public const string Name = "PostgreSQL integration tests";
}

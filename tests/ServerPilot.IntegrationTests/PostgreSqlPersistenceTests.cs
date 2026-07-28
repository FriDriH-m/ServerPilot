using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ServerPilot.Domain.Users;
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
            migration => migration.EndsWith(
                "_AddAgentRegistrationAndAuthentication",
                StringComparison.Ordinal));
        Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DatabaseRejectsInvalidInstallationTokenTimestampsAndHashes()
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ServerPilotDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();
        User user = User.Create(
            Guid.NewGuid(),
            "constraints@example.com",
            "CONSTRAINTS@EXAMPLE.COM",
            "test-password-hash",
            DateTimeOffset.UtcNow);
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        DateTimeOffset createdAt = DateTimeOffset.UtcNow;
        DateTimeOffset expiresAt = createdAt.AddMinutes(15);
        DateTimeOffset invalidUsedAt = createdAt.AddSeconds(-1);

        PostgresException invalidTimestamp = await Assert.ThrowsAsync<PostgresException>(() =>
            dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO agent_installation_tokens
                    (id, user_id, token_hash, created_at, expires_at, used_at, revoked_at)
                VALUES
                    ({Guid.NewGuid()}, {user.Id}, {new string('a', 64)}, {createdAt},
                     {expiresAt}, {invalidUsedAt}, NULL)
                """, CancellationToken.None));
        Assert.Equal(
            "ck_agent_installation_tokens_valid_used_at",
            invalidTimestamp.ConstraintName);

        PostgresException invalidHash = await Assert.ThrowsAsync<PostgresException>(() =>
            dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO agent_installation_tokens
                    (id, user_id, token_hash, created_at, expires_at, used_at, revoked_at)
                VALUES
                    ({Guid.NewGuid()}, {user.Id}, {new string('A', 64)}, {createdAt},
                     {expiresAt}, NULL, NULL)
                """, CancellationToken.None));
        Assert.Equal(
            "ck_agent_installation_tokens_valid_token_hash",
            invalidHash.ConstraintName);
    }

    [Fact]
    public async Task DatabaseRejectsInvalidAgentCredentialState()
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ServerPilotDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();
        DateTimeOffset registeredAt = DateTimeOffset.UtcNow;
        User user = User.Create(
            Guid.NewGuid(),
            "agent-constraints@example.com",
            "AGENT-CONSTRAINTS@EXAMPLE.COM",
            "test-password-hash",
            registeredAt);
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        PostgresException invalidHash = await Assert.ThrowsAsync<PostgresException>(() =>
            dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO agents
                    (id, user_id, name, machine_name, operating_system, agent_version,
                     credential_hash, registered_at, credential_revoked_at)
                VALUES
                    ({Guid.NewGuid()}, {user.Id}, {"Agent"}, {"HOST"}, {"Windows"},
                     {"1.0.0"}, {new string('A', 64)}, {registeredAt}, NULL)
                """, CancellationToken.None));
        Assert.Equal("ck_agents_valid_credential_hash", invalidHash.ConstraintName);

        PostgresException invalidRevocation = await Assert.ThrowsAsync<PostgresException>(() =>
            dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO agents
                    (id, user_id, name, machine_name, operating_system, agent_version,
                     credential_hash, registered_at, credential_revoked_at)
                VALUES
                    ({Guid.NewGuid()}, {user.Id}, {"Agent"}, {"HOST"}, {"Windows"},
                     {"1.0.0"}, {new string('b', 64)}, {registeredAt},
                     {registeredAt.AddTicks(-10)})
                """, CancellationToken.None));
        Assert.Equal(
            "ck_agents_valid_credential_revoked_at",
            invalidRevocation.ConstraintName);
    }
}

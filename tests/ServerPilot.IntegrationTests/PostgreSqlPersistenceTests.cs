using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ServerPilot.Domain.Agents;
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
                "_AddServerInstances",
                StringComparison.Ordinal));
        Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync(CancellationToken.None));

        await dbContext.Database.OpenConnectionAsync(CancellationToken.None);
        await using var indexCommand = dbContext.Database.GetDbConnection().CreateCommand();
        indexCommand.CommandText = """
            SELECT indexdef
            FROM pg_indexes
            WHERE schemaname = 'public'
              AND indexname = 'ix_agents_user_id_registered_at_id'
            """;
        string indexDefinition = Assert.IsType<string>(
            await indexCommand.ExecuteScalarAsync(CancellationToken.None));
        Assert.Contains("user_id", indexDefinition, StringComparison.Ordinal);
        Assert.Contains("registered_at DESC", indexDefinition, StringComparison.Ordinal);
        Assert.Contains("id DESC", indexDefinition, StringComparison.Ordinal);

        await using var serverIndexCommand = dbContext.Database.GetDbConnection().CreateCommand();
        serverIndexCommand.CommandText = """
            SELECT indexdef
            FROM pg_indexes
            WHERE schemaname = 'public'
              AND indexname = 'ix_server_instances_agent_id_created_at_id'
            """;
        string serverIndexDefinition = Assert.IsType<string>(
            await serverIndexCommand.ExecuteScalarAsync(CancellationToken.None));
        Assert.Contains("agent_id", serverIndexDefinition, StringComparison.Ordinal);
        Assert.Contains("created_at DESC", serverIndexDefinition, StringComparison.Ordinal);
        Assert.Contains("id DESC", serverIndexDefinition, StringComparison.Ordinal);
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

        PostgresException invalidLastSeen = await Assert.ThrowsAsync<PostgresException>(() =>
            dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO agents
                    (id, user_id, name, machine_name, operating_system, agent_version,
                     credential_hash, registered_at, last_seen_at, credential_revoked_at)
                VALUES
                    ({Guid.NewGuid()}, {user.Id}, {"Agent"}, {"HOST"}, {"Windows"},
                     {"1.0.0"}, {new string('c', 64)}, {registeredAt},
                     {registeredAt.AddTicks(-10)}, NULL)
                """, CancellationToken.None));
        Assert.Equal(
            "ck_agents_valid_last_seen_at",
            invalidLastSeen.ConstraintName);
    }

    [Fact]
    public async Task DatabaseRejectsInvalidServerInstanceState()
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ServerPilotDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();
        DateTimeOffset createdAt = DateTimeOffset.UtcNow;
        User user = User.Create(
            Guid.NewGuid(),
            "server-constraints@example.com",
            "SERVER-CONSTRAINTS@EXAMPLE.COM",
            "test-password-hash",
            createdAt);
        Agent agent = Agent.Create(
            Guid.NewGuid(),
            user.Id,
            "Agent",
            "HOST",
            "Windows",
            "1.0.0",
            new string('a', Agent.CredentialHashLength),
            createdAt);
        dbContext.AddRange(user, agent);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        PostgresException invalidState = await Assert.ThrowsAsync<PostgresException>(() =>
            dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO server_instances
                    (id, agent_id, name, executable_path, arguments, working_directory,
                     process_name, status, last_process_id, created_at, updated_at)
                VALUES
                    ({Guid.NewGuid()}, {agent.Id}, {"Server"}, {"C:\\Servers\\server.exe"},
                     {""}, {"C:\\Servers"}, {"server.exe"}, 0, NULL,
                     {createdAt}, {createdAt})
                """, CancellationToken.None));
        Assert.Equal("ck_server_instances_valid_state", invalidState.ConstraintName);
    }
}

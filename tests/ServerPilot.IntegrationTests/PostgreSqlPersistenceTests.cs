using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ServerPilot.Domain.Agents;
using ServerPilot.Domain.Commands;
using ServerPilot.Domain.ServerInstances;
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
                "_AddServerCommands",
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

        await using var commandIndexCommand = dbContext.Database.GetDbConnection().CreateCommand();
        commandIndexCommand.CommandText = """
            SELECT indexdef
            FROM pg_indexes
            WHERE schemaname = 'public'
              AND indexname = 'ix_server_commands_agent_id_status_created_at_id'
            """;
        string commandIndexDefinition = Assert.IsType<string>(
            await commandIndexCommand.ExecuteScalarAsync(CancellationToken.None));
        Assert.Contains("agent_id", commandIndexDefinition, StringComparison.Ordinal);
        Assert.Contains("status", commandIndexDefinition, StringComparison.Ordinal);
        Assert.Contains("created_at", commandIndexDefinition, StringComparison.Ordinal);
        Assert.Contains("id", commandIndexDefinition, StringComparison.Ordinal);

        await using var commandHistoryIndexCommand = dbContext.Database.GetDbConnection()
            .CreateCommand();
        commandHistoryIndexCommand.CommandText = """
            SELECT indexdef
            FROM pg_indexes
            WHERE schemaname = 'public'
              AND indexname = 'ix_server_commands_server_instance_id_created_at_id'
            """;
        string commandHistoryIndexDefinition = Assert.IsType<string>(
            await commandHistoryIndexCommand.ExecuteScalarAsync(CancellationToken.None));
        Assert.Contains("server_instance_id", commandHistoryIndexDefinition, StringComparison.Ordinal);
        Assert.Contains("created_at DESC", commandHistoryIndexDefinition, StringComparison.Ordinal);
        Assert.Contains("id DESC", commandHistoryIndexDefinition, StringComparison.Ordinal);
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

        PostgresException invalidProfile = await Assert.ThrowsAsync<PostgresException>(() =>
            dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO server_instances
                    (id, agent_id, profile, name, executable_path, arguments,
                     working_directory, process_name, data_directory, status,
                     last_process_id, created_at, updated_at)
                VALUES
                    ({Guid.NewGuid()}, {agent.Id}, 1, {"Project Zomboid"},
                     {"C:\\Servers\\ProjectZomboid\\StartServer64.bat"}, {"--unsafe"},
                     {"C:\\Servers\\ProjectZomboid"}, {"java"},
                     {"C:\\ServerPilotData\\ProjectZomboid"}, 1, NULL,
                     {createdAt}, {createdAt})
                """, CancellationToken.None));
        Assert.Equal("ck_server_instances_trimmed_configuration", invalidProfile.ConstraintName);
    }

    [Fact]
    public async Task ServerCommandPersistsItsLifecycleAndFailureDetails()
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ServerPilotDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();
        DateTimeOffset createdAt = DateTimeOffset.UtcNow;
        Agent agent = await CreateAgentAsync(dbContext, "command-persistence@example.com", createdAt);
        ServerInstance serverInstance = CreateServerInstance(agent.Id, createdAt);
        ServerCommand command = ServerCommand.Create(
            Guid.NewGuid(),
            agent.Id,
            serverInstance.Id,
            ServerCommandType.StartServer,
            createdAt,
            Guid.NewGuid());
        Assert.True(command.TryClaim(createdAt.AddSeconds(1)));
        Assert.True(command.TryStart(createdAt.AddSeconds(2)));
        Assert.True(command.TryFail(createdAt.AddSeconds(3), " ProcessFailed ", " Process exited. "));

        dbContext.AddRange(serverInstance, command);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        dbContext.ChangeTracker.Clear();

        ServerCommand persisted = await dbContext.ServerCommands
            .AsNoTracking()
            .SingleAsync(item => item.Id == command.Id, CancellationToken.None);

        Assert.Equal(ServerCommandStatus.Failed, persisted.Status);
        Assert.Equal(agent.Id, persisted.AgentId);
        Assert.Equal(serverInstance.Id, persisted.ServerInstanceId);
        Assert.Equal(ServerCommandType.StartServer, persisted.Type);
        Assert.Equal(1, persisted.AttemptCount);
        Assert.Equal("ProcessFailed", persisted.ErrorCode);
        Assert.Equal("Process exited.", persisted.ErrorMessage);
        Assert.Equal(
            createdAt.AddSeconds(3).Ticks / 10,
            Assert.IsType<DateTimeOffset>(persisted.CompletedAt).Ticks / 10);
    }

    [Fact]
    public async Task DatabaseRejectsInvalidCommandStateAndMismatchedServerAgent()
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ServerPilotDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();
        DateTimeOffset createdAt = DateTimeOffset.UtcNow;
        Agent firstAgent = await CreateAgentAsync(
            dbContext,
            "command-first-agent@example.com",
            createdAt);
        Agent secondAgent = await CreateAgentAsync(
            dbContext,
            "command-second-agent@example.com",
            createdAt);
        ServerInstance serverInstance = CreateServerInstance(firstAgent.Id, createdAt);
        dbContext.ServerInstances.Add(serverInstance);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        PostgresException invalidState = await Assert.ThrowsAsync<PostgresException>(() =>
            dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO server_commands
                    (id, agent_id, server_instance_id, type, status, created_at, claimed_at,
                     started_at, completed_at, error_code, error_message, attempt_count,
                     correlation_id)
                VALUES
                    ({Guid.NewGuid()}, {firstAgent.Id}, {serverInstance.Id}, 1, 5, {createdAt},
                     {createdAt.AddSeconds(1)}, {createdAt.AddSeconds(2)},
                     {createdAt.AddSeconds(3)}, NULL, NULL, 1, {Guid.NewGuid()})
                """, CancellationToken.None));
        Assert.Equal("ck_server_commands_valid_failure_details", invalidState.ConstraintName);

        PostgresException mismatchedAgent = await Assert.ThrowsAsync<PostgresException>(() =>
            dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO server_commands
                    (id, agent_id, server_instance_id, type, status, created_at, claimed_at,
                     started_at, completed_at, error_code, error_message, attempt_count,
                     correlation_id)
                VALUES
                    ({Guid.NewGuid()}, {secondAgent.Id}, {serverInstance.Id}, 1, 1, {createdAt},
                     NULL, NULL, NULL, NULL, NULL, 0, {Guid.NewGuid()})
                """, CancellationToken.None));
        Assert.Equal(
            "fk_server_commands_server_instances_agent_id_server_instance_id",
            mismatchedAgent.ConstraintName);
    }

    private static async Task<Agent> CreateAgentAsync(
        ServerPilotDbContext dbContext,
        string email,
        DateTimeOffset createdAt)
    {
        User user = User.Create(
            Guid.NewGuid(),
            email,
            email.ToUpperInvariant(),
            "test-password-hash",
            createdAt);
        Agent agent = Agent.Create(
            Guid.NewGuid(),
            user.Id,
            "Agent",
            "HOST",
            "Windows",
            "1.0.0",
            string.Concat(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N")),
            createdAt);
        dbContext.AddRange(user, agent);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return agent;
    }

    private static ServerInstance CreateServerInstance(Guid agentId, DateTimeOffset createdAt)
    {
        Assert.True(ServerInstanceConfiguration.TryCreate(
            "Server",
            "C:\\Servers\\server.exe",
            string.Empty,
            "C:\\Servers",
            "server.exe",
            out ServerInstanceConfiguration? configuration));

        return ServerInstance.Create(
            Guid.NewGuid(),
            agentId,
            Assert.IsType<ServerInstanceConfiguration>(configuration),
            createdAt);
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServerPilot.Application.Backups;
using ServerPilot.Domain.Backups;
using ServerPilot.Infrastructure.Persistence;
using ServerPilot.IntegrationTests.Infrastructure;

namespace ServerPilot.IntegrationTests;

[Collection(PostgreSqlTestGroup.Name)]
public sealed class LocalBackupTests : IAsyncLifetime, IDisposable
{
    private readonly ServerPilotApiFactory factory;
    private readonly HttpClient client;
    public LocalBackupTests(PostgreSqlDatabaseFixture database)
    {
        factory = new ServerPilotApiFactory(database.ConnectionString);
        client = factory.CreateClient();
    }
    public Task InitializeAsync() => factory.ResetDatabaseAsync(CancellationToken.None);
    public Task DisposeAsync() => Task.CompletedTask;
    public void Dispose() { client.Dispose(); factory.Dispose(); }

    [Fact]
    public async Task OwnerCreatesBackupAndAssignedAgentAtomicallyCompletesMetadataWithExactReplay()
    {
        Setup data = await SetupAsync();
        Guid id = await QueueAsync(data);
        using HttpResponseMessage competing = await SendAsync("Bearer", data.Token, HttpMethod.Post, $"/api/server-instances/{data.ServerId}/commands/start");
        Assert.Equal(HttpStatusCode.Conflict, competing.StatusCode);
        await StartAsync(data, id);
        using HttpResponseMessage incomplete = await SendAsync("Agent", data.Credential, HttpMethod.Post, $"/api/commands/{id}/complete");
        Assert.Equal(HttpStatusCode.Conflict, incomplete.StatusCode);
        var artifact = new { SizeBytes = 120L, Checksum = new string('a', 64) };
        using HttpResponseMessage completed = await SendAsync("Agent", data.Credential, HttpMethod.Post, $"/api/commands/{id}/complete-backup", artifact);
        Assert.Equal(HttpStatusCode.NoContent, completed.StatusCode);
        using HttpResponseMessage replay = await SendAsync("Agent", data.Credential, HttpMethod.Post, $"/api/commands/{id}/complete-backup", artifact);
        Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
        using HttpResponseMessage different = await SendAsync("Agent", data.Credential, HttpMethod.Post, $"/api/commands/{id}/complete-backup", artifact with { SizeBytes = 121L });
        Assert.Equal(HttpStatusCode.Conflict, different.StatusCode);
        using HttpResponseMessage history = await SendAsync("Bearer", data.Token, HttpMethod.Get, $"/api/server-instances/{data.ServerId}/backups");
        string payload = await history.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        BackupDetails backup = Assert.Single(JsonSerializer.Deserialize<History>(payload, JsonSerializerOptions.Web)!.Items);
        Assert.Equal(id, backup.Id);
        Assert.Equal("Completed", backup.Status);
        Assert.Equal(120, backup.SizeBytes);
        Assert.Equal(new string('A', 64), backup.Checksum);
        Assert.NotNull(backup.StartedAt);
        Assert.NotNull(backup.CompletedAt);
        Assert.DoesNotContain("C:\\", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("errorMessage", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FailedBackupHasNoArtifactAndHistorySupportsKeysetPaging()
    {
        Setup data = await SetupAsync();
        Guid first = await QueueAsync(data);
        await StartAsync(data, first);
        using HttpResponseMessage failed = await SendAsync("Agent", data.Credential, HttpMethod.Post, $"/api/commands/{first}/fail",
            new { ErrorCode = "BackupFileOperationFailed", ErrorMessage = "Safe failure message" });
        Assert.Equal(HttpStatusCode.NoContent, failed.StatusCode);
        Guid second = await QueueAsync(data);
        using HttpResponseMessage page = await SendAsync("Bearer", data.Token, HttpMethod.Get, $"/api/server-instances/{data.ServerId}/backups?limit=1");
        History firstPage = (await page.Content.ReadFromJsonAsync<History>())!;
        Assert.Equal(second, Assert.Single(firstPage.Items).Id);
        Assert.NotNull(firstPage.NextCursor);
        using HttpResponseMessage older = await SendAsync("Bearer", data.Token, HttpMethod.Get,
            $"/api/server-instances/{data.ServerId}/backups?limit=1&cursor={Uri.EscapeDataString(firstPage.NextCursor)}");
        BackupDetails backup = Assert.Single((await older.Content.ReadFromJsonAsync<History>())!.Items);
        Assert.Equal(first, backup.Id);
        Assert.Equal("Failed", backup.Status);
        Assert.Null(backup.SizeBytes);
        Assert.Null(backup.Checksum);
        Assert.Equal("BackupFileOperationFailed", backup.ErrorCode);
    }

    [Fact]
    public async Task OwnershipAuthenticationValidationAndStoppedPreconditionAreEnforced()
    {
        Setup data = await SetupAsync(stopped: false);
        using HttpResponseMessage notStopped = await SendAsync("Bearer", data.Token, HttpMethod.Post, $"/api/server-instances/{data.ServerId}/commands/backup");
        Assert.Equal(HttpStatusCode.Conflict, notStopped.StatusCode);
        await ReportStoppedAsync(data);
        Guid id = await QueueAsync(data);
        Setup stranger = await SetupAsync();
        using HttpResponseMessage foreignRead = await SendAsync("Bearer", stranger.Token, HttpMethod.Get, $"/api/server-instances/{data.ServerId}/backups");
        using HttpResponseMessage foreignCreate = await SendAsync("Bearer", stranger.Token, HttpMethod.Post, $"/api/server-instances/{data.ServerId}/commands/backup");
        Assert.Equal(HttpStatusCode.NotFound, foreignRead.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignCreate.StatusCode);
        await StartAsync(data, id);
        using HttpResponseMessage foreignResult = await SendAsync("Agent", stranger.Credential, HttpMethod.Post, $"/api/commands/{id}/complete-backup",
            new { SizeBytes = 10, Checksum = new string('A', 64) });
        Assert.Equal(HttpStatusCode.NotFound, foreignResult.StatusCode);
        using HttpResponseMessage invalidResult = await SendAsync("Agent", data.Credential, HttpMethod.Post, $"/api/commands/{id}/complete-backup",
            new { SizeBytes = 0, Checksum = "not-a-checksum" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidResult.StatusCode);
        using HttpResponseMessage anonymous = await client.GetAsync($"/api/server-instances/{data.ServerId}/backups");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using HttpResponseMessage cursor = await SendAsync("Bearer", data.Token, HttpMethod.Get, $"/api/server-instances/{data.ServerId}/backups?cursor=invalid");
        Assert.Equal(HttpStatusCode.BadRequest, cursor.StatusCode);
    }

    [Fact]
    public async Task ConcurrentEnqueueCreatesOneCommandAndOneBackup()
    {
        Setup data = await SetupAsync();
        HttpResponseMessage[] responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            SendAsync("Bearer", data.Token, HttpMethod.Post, $"/api/server-instances/{data.ServerId}/commands/backup")));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();
            Assert.Equal(1, await db.Backups.CountAsync());
            Assert.Equal(1, await db.ServerCommands.CountAsync());
            await Assert.ThrowsAsync<DbUpdateException>(async () =>
            {
                db.Backups.Add(Backup.Create(Guid.NewGuid()));
                await db.SaveChangesAsync();
            });
        }
        finally { foreach (HttpResponseMessage response in responses) response.Dispose(); }
    }

    private async Task<Guid> QueueAsync(Setup data)
    {
        using HttpResponseMessage response = await SendAsync("Bearer", data.Token, HttpMethod.Post, $"/api/server-instances/{data.ServerId}/commands/backup");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Identifier>())!.Id;
    }

    private async Task StartAsync(Setup data, Guid id)
    {
        using HttpResponseMessage claim = await SendAsync("Agent", data.Credential, HttpMethod.Post, $"/api/agents/{data.AgentId}/commands/claim-next");
        Assert.Equal(HttpStatusCode.OK, claim.StatusCode);
        Assert.Contains("CreateBackup", await claim.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        using HttpResponseMessage start = await SendAsync("Agent", data.Credential, HttpMethod.Post, $"/api/commands/{id}/start");
        Assert.Equal(HttpStatusCode.NoContent, start.StatusCode);
    }

    private async Task<Setup> SetupAsync(bool stopped = true)
    {
        using HttpResponseMessage registration = await client.PostAsJsonAsync("/api/auth/register",
            new { Email = $"backup-{Guid.NewGuid():N}@example.com", Password = "correct horse battery staple" });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        string token = (await registration.Content.ReadFromJsonAsync<Authentication>())!.AccessToken;
        using HttpResponseMessage installation = await SendAsync("Bearer", token, HttpMethod.Post, "/api/agent-installation-tokens");
        string installationToken = (await installation.Content.ReadFromJsonAsync<Installation>())!.Token;
        using HttpResponseMessage agentResponse = await client.PostAsJsonAsync("/api/agents/register",
            new { InstallationToken = installationToken, Name = "Backup Agent", MachineName = "TEST", OperatingSystem = "Windows", Version = "1.0" });
        Agent agent = (await agentResponse.Content.ReadFromJsonAsync<Agent>())!;
        using HttpResponseMessage serverResponse = await SendAsync("Bearer", token, HttpMethod.Post, "/api/server-instances",
            new
            {
                agent.AgentId,
                Profile = "ProjectZomboid",
                Name = "Backup Test",
                ExecutablePath = @"C:\Servers\ProjectZomboid\StartServer64.bat",
                Arguments = "",
                WorkingDirectory = "",
                ProcessName = "",
                DataDirectory = @"C:\ServerPilotData\ProjectZomboid"
            });
        Assert.Equal(HttpStatusCode.Created, serverResponse.StatusCode);
        Guid serverId = (await serverResponse.Content.ReadFromJsonAsync<Identifier>())!.Id;
        var data = new Setup(token, agent.AgentId, agent.Credential, serverId);
        if (stopped) await ReportStoppedAsync(data);
        return data;
    }

    private async Task ReportStoppedAsync(Setup data)
    {
        using HttpResponseMessage state = await SendAsync("Agent", data.Credential, HttpMethod.Post,
            $"/api/agents/{data.AgentId}/server-instances/{data.ServerId}/status", new { Status = "Stopped" });
        using HttpResponseMessage heartbeat = await SendAsync("Agent", data.Credential, HttpMethod.Post, $"/api/agents/{data.AgentId}/heartbeat");
        Assert.Equal(HttpStatusCode.NoContent, state.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, heartbeat.StatusCode);
    }

    private async Task<HttpResponseMessage> SendAsync(string scheme, string token, HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue(scheme, token);
        return await client.SendAsync(request);
    }

    private sealed record Setup(string Token, Guid AgentId, string Credential, Guid ServerId);
    private sealed record Authentication(string AccessToken);
    private sealed record Installation(string Token);
    private sealed record Agent(Guid AgentId, string Credential);
    private sealed record Identifier(Guid Id);
    private sealed record History(IReadOnlyList<BackupDetails> Items, string? NextCursor);
}

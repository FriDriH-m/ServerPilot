using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ServerPilot.Agent.Credentials;

namespace ServerPilot.Agent.Api;

public sealed class HttpAgentApiClient(HttpClient httpClient) : IAgentApiClient
{
    public async Task SendHeartbeatAsync(
        AgentCredential credential,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = CreateRequest(
            HttpMethod.Post,
            $"api/agents/{credential.AgentId}/heartbeat",
            credential);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        EnsureStatus(response, HttpStatusCode.NoContent);
    }

    public async Task<ClaimedAgentCommand?> ClaimNextCommandAsync(
        AgentCredential credential,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = CreateRequest(
            HttpMethod.Post,
            $"api/agents/{credential.AgentId}/commands/claim-next",
            credential);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }

        EnsureStatus(response, HttpStatusCode.OK);

        ClaimNextResponse? command;
        try
        {
            command = await response.Content.ReadFromJsonAsync<ClaimNextResponse>(cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new AgentApiException(
                "Agent command claim response has an invalid JSON payload.",
                AgentApiFailureKind.Configuration,
                exception);
        }

        if (command is null ||
            command.Id == Guid.Empty ||
            command.ServerInstanceId == Guid.Empty ||
            command.CorrelationId == Guid.Empty ||
            string.IsNullOrWhiteSpace(command.Type) ||
            string.IsNullOrWhiteSpace(command.DeliveryKind))
        {
            throw new AgentApiException(
                "Agent command claim response is missing required fields.",
                AgentApiFailureKind.Configuration);
        }

        if (command.AgentId != credential.AgentId)
        {
            throw new AgentApiException(
                "Agent command claim response belongs to another Agent.",
                AgentApiFailureKind.Configuration);
        }

        return new ClaimedAgentCommand(
            command.Id,
            command.ServerInstanceId,
            command.Type,
            command.CorrelationId,
            command.DeliveryKind);
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string path,
        AgentCredential credential)
    {
        HttpRequestMessage request = new(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            credential.AuthorizationScheme,
            credential.Value);
        return request;
    }

    private static void EnsureStatus(HttpResponseMessage response, HttpStatusCode expectedStatus)
    {
        if (response.StatusCode != expectedStatus)
        {
            throw new AgentApiException(response.StatusCode);
        }
    }

    private sealed class ClaimNextResponse
    {
        public Guid Id { get; init; }

        public Guid AgentId { get; init; }

        public Guid ServerInstanceId { get; init; }

        public string? Type { get; init; }

        public Guid CorrelationId { get; init; }

        public string? DeliveryKind { get; init; }
    }
}

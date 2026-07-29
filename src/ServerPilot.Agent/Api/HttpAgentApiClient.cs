using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ServerPilot.Agent.Credentials;

namespace ServerPilot.Agent.Api;

public sealed class HttpAgentApiClient(HttpClient httpClient) : IAgentApiClient
{
    private const string CorrelationIdHeaderName = "X-Correlation-ID";

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
            string.IsNullOrWhiteSpace(command.DeliveryKind) ||
            command.ServerInstance is null ||
            string.IsNullOrWhiteSpace(command.ServerInstance.ExecutablePath) ||
            command.ServerInstance.Arguments is null ||
            string.IsNullOrWhiteSpace(command.ServerInstance.WorkingDirectory) ||
            string.IsNullOrWhiteSpace(command.ServerInstance.ProcessName))
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

        if (!Enum.TryParse(command.Type, ignoreCase: false, out AgentCommandType commandType) ||
            !Enum.IsDefined(commandType))
        {
            throw new AgentApiException(
                "Agent command claim response has an unsupported command type.",
                AgentApiFailureKind.Configuration);
        }

        return new ClaimedAgentCommand(
            command.Id,
            command.ServerInstanceId,
            commandType,
            command.CorrelationId,
            command.DeliveryKind,
            new ClaimedAgentServerInstance(
                command.ServerInstance.ExecutablePath,
                command.ServerInstance.Arguments,
                command.ServerInstance.WorkingDirectory,
                command.ServerInstance.ProcessName));
    }

    public Task MarkCommandRunningAsync(
        AgentCredential credential,
        ClaimedAgentCommand command,
        CancellationToken cancellationToken) =>
        SendTransitionAsync(
            credential,
            command,
            "start",
            content: null,
            cancellationToken);

    public Task CompleteCommandAsync(
        AgentCredential credential,
        ClaimedAgentCommand command,
        CancellationToken cancellationToken) =>
        SendTransitionAsync(
            credential,
            command,
            "complete",
            content: null,
            cancellationToken);

    public Task FailCommandAsync(
        AgentCredential credential,
        ClaimedAgentCommand command,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken) =>
        SendTransitionAsync(
            credential,
            command,
            "fail",
            JsonContent.Create(new FailCommandRequest(errorCode, errorMessage)),
            cancellationToken);

    private async Task SendTransitionAsync(
        AgentCredential credential,
        ClaimedAgentCommand command,
        string transition,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = CreateRequest(
            HttpMethod.Post,
            $"api/commands/{command.Id}/{transition}",
            credential,
            command.CorrelationId);
        request.Content = content;
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        EnsureStatus(response, HttpStatusCode.NoContent);
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string path,
        AgentCredential credential,
        Guid? correlationId = null)
    {
        HttpRequestMessage request = new(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            credential.AuthorizationScheme,
            credential.Value);
        if (correlationId.HasValue)
        {
            request.Headers.Add(CorrelationIdHeaderName, correlationId.Value.ToString("D"));
        }

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

        public ClaimServerInstanceResponse? ServerInstance { get; init; }
    }

    private sealed class ClaimServerInstanceResponse
    {
        public string? ExecutablePath { get; init; }

        public string? Arguments { get; init; }

        public string? WorkingDirectory { get; init; }

        public string? ProcessName { get; init; }
    }

    private sealed record FailCommandRequest(string ErrorCode, string ErrorMessage);
}

using System.Net;
using System.Net.Http.Json;
using ServerPilot.Agent.Credentials;

namespace ServerPilot.Agent.Registration;

public sealed class HttpAgentRegistrationClient(HttpClient httpClient) : IAgentRegistrationClient
{
    private const string RegistrationPath = "api/agents/register";

    public async Task<AgentCredential> RegisterAsync(
        AgentRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            RegistrationPath,
            request,
            cancellationToken);
        if (response.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException(
                $"Agent registration failed with HTTP {(int)response.StatusCode}.");
        }

        RegistrationResponse? registration = await response.Content.ReadFromJsonAsync<RegistrationResponse>(
            cancellationToken);
        if (registration is null)
        {
            throw new InvalidOperationException("Agent registration returned an empty response.");
        }

        return AgentCredential.Create(
            registration.AgentId,
            registration.Credential,
            registration.AuthorizationScheme);
    }

    private sealed class RegistrationResponse
    {
        public Guid AgentId { get; init; }

        public string? Credential { get; init; }

        public string? AuthorizationScheme { get; init; }
    }
}

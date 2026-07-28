using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ServerPilot.Api.Authentication;
using ServerPilot.Api.Contracts.Agents;
using ServerPilot.Api.Http;
using ServerPilot.Application.Agents;
using ServerPilot.Application.Authentication;
using ServerPilot.Infrastructure.Authentication;

namespace ServerPilot.Api.Controllers;

[ApiController]
[Route("api/agents")]
public sealed class AgentsController(
    AgentRegistrationService registration,
    AgentHeartbeatService heartbeat,
    AgentQueryService queries,
    AgentManagementService management,
    ICurrentAgent currentAgent,
    ICurrentUser currentUser,
    ILogger<AgentsController> logger) : ControllerBase
{
    private const int DefaultListLimit = 50;
    private const int MaximumListLimit = 100;
    private const int MaximumListPage = 1_000;

    private static readonly Action<ILogger, Exception?> LogAgentRegistrationRejected =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1300, nameof(LogAgentRegistrationRejected)),
            "Agent registration rejected because the installation token is invalid or inactive");
    private static readonly Action<ILogger, Guid, Guid, Exception?> LogAgentRegistered =
        LoggerMessage.Define<Guid, Guid>(
            LogLevel.Information,
            new EventId(1301, nameof(LogAgentRegistered)),
            "Registered Agent {AgentId} for user {UserId}");
    private static readonly Action<ILogger, Guid, Guid, Exception?> LogAgentCredentialsRevoked =
        LoggerMessage.Define<Guid, Guid>(
            LogLevel.Information,
            new EventId(1302, nameof(LogAgentCredentialsRevoked)),
            "User {UserId} revoked credentials for Agent {AgentId}");
    private static readonly Action<ILogger, Guid, Guid, Exception?> LogAgentRevokeNotFound =
        LoggerMessage.Define<Guid, Guid>(
            LogLevel.Warning,
            new EventId(1303, nameof(LogAgentRevokeNotFound)),
            "User {UserId} attempted to revoke credentials for missing or foreign Agent {AgentId}");
    private static readonly Action<ILogger, Guid, Exception?> LogAgentHeartbeatRecorded =
        LoggerMessage.Define<Guid>(
            LogLevel.Debug,
            new EventId(1304, nameof(LogAgentHeartbeatRecorded)),
            "Recorded heartbeat for Agent {AgentId}");
    private static readonly Action<ILogger, Guid, Guid, Exception?> LogForeignAgentHeartbeat =
        LoggerMessage.Define<Guid, Guid>(
            LogLevel.Warning,
            new EventId(1305, nameof(LogForeignAgentHeartbeat)),
            "Agent {AgentId} attempted heartbeat for Agent {RequestedAgentId}");
    private static readonly Action<ILogger, Guid, Guid, Exception?> LogOwnedAgentNotFound =
        LoggerMessage.Define<Guid, Guid>(
            LogLevel.Warning,
            new EventId(1306, nameof(LogOwnedAgentNotFound)),
            "User {UserId} attempted to read missing or foreign Agent {AgentId}");

    [AllowAnonymous]
    [HttpPost("register")]
    [EnableRateLimiting(ApiRateLimitPolicyNames.Authentication)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<RegisterAgentResponse>> Register(
        RegisterAgentRequest request,
        CancellationToken cancellationToken)
    {
        RegisterAgentResult result = await registration.RegisterAsync(
            request.InstallationToken!,
            request.Name!,
            request.MachineName!,
            request.OperatingSystem!,
            request.Version!,
            cancellationToken);
        if (result.Status == RegisterAgentStatus.InvalidInstallationToken)
        {
            LogAgentRegistrationRejected(logger, null);
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "The installation token is invalid or inactive.");
        }

        if (result.Status != RegisterAgentStatus.Succeeded || result.Agent is null)
        {
            throw new InvalidOperationException(
                $"Unsupported Agent registration status '{result.Status}'.");
        }

        RegisteredAgent agent = result.Agent;
        LogAgentRegistered(logger, agent.Id, agent.UserId, null);
        RegisterAgentResponse response = new(
            agent.Id,
            agent.RawCredential,
            AgentAuthenticationDefaults.AuthenticationScheme,
            agent.RegisteredAt);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    [Authorize(Policy = AgentAuthorizationPolicyNames.Agent)]
    [HttpGet("me")]
    public ActionResult<CurrentAgentResponse> GetCurrent()
    {
        return currentAgent.AgentId is Guid agentId
            ? Ok(new CurrentAgentResponse(agentId))
            : Unauthorized();
    }

    [Authorize(Policy = AgentAuthorizationPolicyNames.Agent)]
    [HttpPost("{id:guid}/heartbeat")]
    public async Task<IActionResult> RecordHeartbeat(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (currentAgent.AgentId is not Guid authenticatedAgentId)
        {
            return Unauthorized();
        }

        if (id != authenticatedAgentId)
        {
            LogForeignAgentHeartbeat(logger, authenticatedAgentId, id, null);
            return NotFound();
        }

        await heartbeat.RecordAsync(authenticatedAgentId, cancellationToken);
        LogAgentHeartbeatRecorded(logger, authenticatedAgentId, null);
        return NoContent();
    }

    [Authorize]
    [EnableRateLimiting(ApiRateLimitPolicyNames.AuthenticatedUser)]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AgentResponse>>> List(
        CancellationToken cancellationToken,
        [FromQuery, Range(1, MaximumListLimit)] int limit = DefaultListLimit,
        [FromQuery, Range(1, MaximumListPage)] int page = 1)
    {
        if (currentUser.UserId is not Guid userId)
        {
            return Unauthorized();
        }

        IReadOnlyList<AgentDetails> agents = await queries.ListAsync(
            userId,
            page,
            limit,
            cancellationToken);
        return Ok(agents.Select(ToResponse).ToArray());
    }

    [Authorize]
    [EnableRateLimiting(ApiRateLimitPolicyNames.AuthenticatedUser)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AgentResponse>> Get(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
        {
            return Unauthorized();
        }

        AgentDetails? agent = await queries.GetAsync(id, userId, cancellationToken);
        if (agent is null)
        {
            LogOwnedAgentNotFound(logger, userId, id, null);
            return NotFound();
        }

        return Ok(ToResponse(agent));
    }

    [Authorize]
    [EnableRateLimiting(ApiRateLimitPolicyNames.AuthenticatedUser)]
    [HttpDelete("{id:guid}/credentials")]
    public async Task<IActionResult> RevokeCredentials(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
        {
            return Unauthorized();
        }

        RevokeAgentCredentialStatus status = await management.RevokeCredentialsAsync(
            id,
            userId,
            cancellationToken);
        if (status is RevokeAgentCredentialStatus.Succeeded or
            RevokeAgentCredentialStatus.AlreadyRevoked)
        {
            LogAgentCredentialsRevoked(logger, userId, id, null);
            return NoContent();
        }

        if (status == RevokeAgentCredentialStatus.NotFound)
        {
            LogAgentRevokeNotFound(logger, userId, id, null);
            return NotFound();
        }

        throw new InvalidOperationException(
            $"Unsupported Agent credential revocation status '{status}'.");
    }

    private static AgentResponse ToResponse(AgentDetails agent) =>
        new(
            agent.Id,
            agent.Name,
            agent.MachineName,
            agent.OperatingSystem,
            agent.Version,
            agent.RegisteredAt,
            agent.LastSeenAt,
            agent.Status.ToString());
}

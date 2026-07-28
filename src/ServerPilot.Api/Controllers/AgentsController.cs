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
    AgentManagementService management,
    ICurrentAgent currentAgent,
    ICurrentUser currentUser,
    ILogger<AgentsController> logger) : ControllerBase
{
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

    [Authorize]
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
}

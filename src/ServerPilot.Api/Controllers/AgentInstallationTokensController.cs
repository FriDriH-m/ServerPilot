using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ServerPilot.Api.Contracts.InstallationTokens;
using ServerPilot.Api.Http;
using ServerPilot.Application.Authentication;
using ServerPilot.Application.InstallationTokens;

namespace ServerPilot.Api.Controllers;

[ApiController]
[Authorize]
[EnableRateLimiting(ApiRateLimitPolicyNames.AuthenticatedUser)]
[Route("api/agent-installation-tokens")]
public sealed class AgentInstallationTokensController(
    AgentInstallationTokenService installationTokens,
    ICurrentUser currentUser,
    ILogger<AgentInstallationTokensController> logger) : ControllerBase
{
    private const int DefaultListLimit = 50;
    private const int MaximumListLimit = 100;
    private const int MaximumListPage = 1_000;

    private static readonly Action<ILogger, Guid, Guid, DateTimeOffset, Exception?>
        LogInstallationTokenCreated = LoggerMessage.Define<Guid, Guid, DateTimeOffset>(
            LogLevel.Information,
            new EventId(1100, nameof(LogInstallationTokenCreated)),
            "User {UserId} created Agent installation token {InstallationTokenId} expiring at {ExpiresAt}");
    private static readonly Action<ILogger, Guid, Exception?> LogActiveTokenLimitReached =
        LoggerMessage.Define<Guid>(
            LogLevel.Warning,
            new EventId(1101, nameof(LogActiveTokenLimitReached)),
            "User {UserId} reached the active Agent installation token limit");
    private static readonly Action<ILogger, Guid, Guid, Exception?> LogInstallationTokenRevoked =
        LoggerMessage.Define<Guid, Guid>(
            LogLevel.Information,
            new EventId(1102, nameof(LogInstallationTokenRevoked)),
            "User {UserId} revoked Agent installation token {InstallationTokenId}");
    private static readonly Action<ILogger, Guid, Guid, Exception?> LogInstallationTokenNotFound =
        LoggerMessage.Define<Guid, Guid>(
            LogLevel.Warning,
            new EventId(1103, nameof(LogInstallationTokenNotFound)),
            "User {UserId} attempted to revoke missing or foreign Agent installation token {InstallationTokenId}");
    private static readonly Action<ILogger, Guid, Guid, Exception?> LogUsedInstallationTokenRevoke =
        LoggerMessage.Define<Guid, Guid>(
            LogLevel.Warning,
            new EventId(1104, nameof(LogUsedInstallationTokenRevoke)),
            "User {UserId} attempted to revoke used Agent installation token {InstallationTokenId}");

    [HttpPost]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<CreateAgentInstallationTokenResponse>> Create(
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
        {
            return Unauthorized();
        }

        CreateAgentInstallationTokenResult result = await installationTokens.CreateAsync(
            userId,
            cancellationToken);
        if (result.Status == CreateAgentInstallationTokenStatus.ActiveLimitReached)
        {
            LogActiveTokenLimitReached(logger, userId, null);
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Conflict",
                detail: "The maximum number of active Agent installation tokens has been reached.");
        }

        CreatedAgentInstallationToken token = result.Token!;
        CreateAgentInstallationTokenResponse response = new(
            token.Id,
            token.RawToken,
            token.CreatedAt,
            token.ExpiresAt);

        LogInstallationTokenCreated(logger, userId, token.Id, token.ExpiresAt, null);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AgentInstallationTokenResponse>>> List(
        CancellationToken cancellationToken,
        [FromQuery, Range(1, MaximumListLimit)] int limit = DefaultListLimit,
        [FromQuery, Range(1, MaximumListPage)] int page = 1)
    {
        if (currentUser.UserId is not Guid userId)
        {
            return Unauthorized();
        }

        IReadOnlyList<AgentInstallationTokenMetadata> tokens =
            await installationTokens.ListAsync(userId, page, limit, cancellationToken);
        AgentInstallationTokenResponse[] response = tokens
            .Select(token => new AgentInstallationTokenResponse(
                token.Id,
                token.CreatedAt,
                token.ExpiresAt,
                token.UsedAt,
                token.RevokedAt,
                token.State.ToString()))
            .ToArray();

        return Ok(response);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
        {
            return Unauthorized();
        }

        RevokeAgentInstallationTokenStatus status = await installationTokens.RevokeAsync(
            id,
            userId,
            cancellationToken);

        if (status is RevokeAgentInstallationTokenStatus.Succeeded or
            RevokeAgentInstallationTokenStatus.AlreadyRevoked)
        {
            LogInstallationTokenRevoked(logger, userId, id, null);
            return NoContent();
        }

        if (status == RevokeAgentInstallationTokenStatus.NotFound)
        {
            LogInstallationTokenNotFound(logger, userId, id, null);
            return NotFound();
        }

        if (status == RevokeAgentInstallationTokenStatus.AlreadyUsed)
        {
            LogUsedInstallationTokenRevoke(logger, userId, id, null);
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Conflict",
                detail: "A used Agent installation token cannot be revoked.");
        }

        throw new InvalidOperationException(
            $"Unsupported installation token revocation status '{status}'.");
    }
}

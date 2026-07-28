using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServerPilot.Api.Contracts.InstallationTokens;
using ServerPilot.Application.Authentication;
using ServerPilot.Application.InstallationTokens;

namespace ServerPilot.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/agent-installation-tokens")]
public sealed class AgentInstallationTokensController(
    AgentInstallationTokenService installationTokens,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<CreateAgentInstallationTokenResponse>> Create(
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
        {
            return Unauthorized();
        }

        CreatedAgentInstallationToken token = await installationTokens.CreateAsync(
            userId,
            cancellationToken);
        CreateAgentInstallationTokenResponse response = new(
            token.Id,
            token.RawToken,
            token.CreatedAt,
            token.ExpiresAt);

        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AgentInstallationTokenResponse>>> List(
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
        {
            return Unauthorized();
        }

        IReadOnlyList<AgentInstallationTokenMetadata> tokens =
            await installationTokens.ListAsync(userId, cancellationToken);
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

        return status switch
        {
            RevokeAgentInstallationTokenStatus.Succeeded => NoContent(),
            RevokeAgentInstallationTokenStatus.NotFound => NotFound(),
            RevokeAgentInstallationTokenStatus.AlreadyUsed => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Conflict",
                detail: "A used Agent installation token cannot be revoked."),
            _ => throw new InvalidOperationException(
                $"Unsupported installation token revocation status '{status}'."),
        };
    }
}

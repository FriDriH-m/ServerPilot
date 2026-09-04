using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ServerPilot.Api.Authentication;
using ServerPilot.Api.Contracts.Commands;
using ServerPilot.Api.Http;
using ServerPilot.Application.Authentication;
using ServerPilot.Application.Backups;
using ServerPilot.Application.Commands;

namespace ServerPilot.Api.Controllers;

[ApiController]
[Authorize]
[EnableRateLimiting(ApiRateLimitPolicyNames.AuthenticatedUser)]
[Route("api/server-instances/{serverInstanceId:guid}/backups")]
public sealed class BackupsController(BackupService backups, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid serverInstanceId, CancellationToken cancellationToken,
        [FromQuery, Range(1, 100)] int limit = 50, [FromQuery] string? cursor = null)
    {
        if (currentUser.UserId is not Guid userId) return Unauthorized();
        ServerCommandHistoryCursor? after = null;
        if (cursor is not null && !ServerCommandCursorCodec.TryDecode(cursor, out after))
        {
            ModelState.AddModelError(nameof(cursor), "Invalid backup cursor.");
            return ValidationProblem(ModelState);
        }

        BackupPage result = await backups.ListAsync(serverInstanceId, userId, after, limit, cancellationToken);
        if (!result.ServerInstanceFound) return NotFound();
        BackupDetails? last = result.Items.Count == 0 ? null : result.Items[^1];
        string? nextCursor = result.HasMore && last is not null
            ? ServerCommandCursorCodec.Encode(last.CreatedAt, last.Id)
            : null;
        return Ok(new BackupHistoryResponse(result.Items, nextCursor));
    }
}

public sealed record BackupHistoryResponse(IReadOnlyList<BackupDetails> Items, string? NextCursor);

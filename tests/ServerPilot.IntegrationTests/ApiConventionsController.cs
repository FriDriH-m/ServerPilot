using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ServerPilot.IntegrationTests;

[ApiController]
[Route("_test/api-conventions")]
public sealed class ApiConventionsController : ControllerBase
{
    [HttpPost("validation")]
    public ActionResult<ValidationResponse> Validate(ValidationRequest request) =>
        Ok(new ValidationResponse(request.Name!));

    [HttpGet("not-found")]
    public IActionResult ReturnNotFound() => NotFound();

    [HttpGet("conflict")]
    public IActionResult ReturnConflict() => Conflict();

    [HttpGet("unauthorized")]
    public IActionResult ReturnUnauthorized() => Unauthorized();

    [HttpGet("forbidden")]
    public IActionResult ReturnForbidden() => StatusCode(StatusCodes.Status403Forbidden);

    [HttpGet("unexpected")]
    public IActionResult ThrowUnexpected() =>
        throw new InvalidOperationException(
            $"Sensitive internal failure from integration test at {Request.Path}.");
}

public sealed record ValidationRequest(
    [Required, StringLength(32, MinimumLength = 1)] string? Name);

public sealed record ValidationResponse(string Name);

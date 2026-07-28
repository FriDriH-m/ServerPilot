using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServerPilot.Api.Contracts.Authentication;
using ServerPilot.Application.Authentication;

namespace ServerPilot.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    UserAuthenticationService authentication,
    ICurrentUser currentUser) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<ActionResult<AuthenticationResponse>> Register(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        RegisterUserResult result = await authentication.RegisterAsync(
            request.Email!,
            request.Password!,
            cancellationToken);

        if (result.Status == RegisterUserStatus.DuplicateEmail)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Conflict",
                detail: "A user with this email already exists.");
        }

        AuthenticationResponse response = MapResponse(result.Session!);
        return CreatedAtAction(nameof(GetCurrentUser), response);
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<AuthenticationResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        AuthenticationSession? session = await authentication.LoginAsync(
            request.Email!,
            request.Password!,
            cancellationToken);
        if (session is null)
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "Invalid email or password.");
        }

        return Ok(MapResponse(session));
    }

    [Authorize]
    [HttpGet("me")]
    public ActionResult<CurrentUserResponse> GetCurrentUser()
    {
        Guid? userId = currentUser.UserId;
        return userId.HasValue
            ? Ok(new CurrentUserResponse(userId.Value))
            : Unauthorized();
    }

    private static AuthenticationResponse MapResponse(AuthenticationSession session) =>
        new(
            session.UserId,
            session.Email,
            session.AccessToken.Value,
            session.AccessToken.ExpiresAt);
}

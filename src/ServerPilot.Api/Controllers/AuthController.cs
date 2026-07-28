using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ServerPilot.Api.Contracts.Authentication;
using ServerPilot.Api.Http;
using ServerPilot.Application.Authentication;

namespace ServerPilot.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    UserAuthenticationService authentication,
    ICurrentUser currentUser,
    ILogger<AuthController> logger) : ControllerBase
{
    private static readonly Action<ILogger, Exception?> LogDuplicateRegistration =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1000, nameof(LogDuplicateRegistration)),
            "User registration rejected because the normalized email already exists");
    private static readonly Action<ILogger, Guid, Exception?> LogUserRegistered =
        LoggerMessage.Define<Guid>(
            LogLevel.Information,
            new EventId(1001, nameof(LogUserRegistered)),
            "Registered user {UserId}");
    private static readonly Action<ILogger, Exception?> LogLoginFailed =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1002, nameof(LogLoginFailed)),
            "User login failed");
    private static readonly Action<ILogger, Guid, Exception?> LogLoginSucceeded =
        LoggerMessage.Define<Guid>(
            LogLevel.Information,
            new EventId(1003, nameof(LogLoginSucceeded)),
            "User {UserId} logged in");

    [AllowAnonymous]
    [HttpPost("register")]
    [EnableRateLimiting(ApiRateLimitPolicyNames.Authentication)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
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
            LogDuplicateRegistration(logger, null);
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Conflict",
                detail: "A user with this email already exists.");
        }

        AuthenticationResponse response = MapResponse(result.Session!);
        LogUserRegistered(logger, response.UserId, null);
        return CreatedAtAction(nameof(GetCurrentUser), response);
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [EnableRateLimiting(ApiRateLimitPolicyNames.Authentication)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
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
            LogLoginFailed(logger, null);
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "Invalid email or password.");
        }

        LogLoginSucceeded(logger, session.UserId, null);
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

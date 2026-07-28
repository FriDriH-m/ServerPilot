using System.IdentityModel.Tokens.Jwt;
using ServerPilot.Application.Authentication;

namespace ServerPilot.Api.Authentication;

internal sealed class HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor)
    : ICurrentUser
{
    public Guid? UserId
    {
        get
        {
            string? subject = httpContextAccessor.HttpContext?.User.FindFirst(
                JwtRegisteredClaimNames.Sub)?.Value;
            return Guid.TryParse(subject, out Guid userId) ? userId : null;
        }
    }
}

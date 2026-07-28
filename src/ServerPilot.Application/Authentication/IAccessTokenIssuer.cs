using ServerPilot.Domain.Users;

namespace ServerPilot.Application.Authentication;

public interface IAccessTokenIssuer
{
    AccessToken Issue(User user, DateTimeOffset issuedAt);
}

namespace ServerPilot.Application.Authentication;

public interface ICurrentUser
{
    Guid? UserId { get; }
}

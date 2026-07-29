namespace ServerPilot.Application.Commands;

public enum AgentCommandTransitionStatus
{
    Succeeded = 1,
    AlreadyApplied = 2,
    NotFound = 3,
    InvalidState = 4,
    InvalidFailureDetails = 5,
}

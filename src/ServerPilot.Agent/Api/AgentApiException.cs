using System.Net;

namespace ServerPilot.Agent.Api;

public enum AgentApiFailureKind
{
    Authentication,
    Configuration,
    Transient,
}

public sealed class AgentApiException : Exception
{
    public AgentApiException(HttpStatusCode statusCode)
        : this(
            $"Agent API request failed with HTTP {(int)statusCode}.",
            GetFailureKind(statusCode),
            statusCode,
            null)
    {
    }

    public AgentApiException(string message, AgentApiFailureKind failureKind)
        : this(message, failureKind, null, null)
    {
    }

    public AgentApiException(
        string message,
        AgentApiFailureKind failureKind,
        Exception innerException)
        : this(message, failureKind, null, innerException)
    {
    }

    private AgentApiException(
        string message,
        AgentApiFailureKind failureKind,
        HttpStatusCode? statusCode,
        Exception? innerException)
        : base(message, innerException)
    {
        FailureKind = failureKind;
        StatusCode = statusCode;
    }

    public AgentApiFailureKind FailureKind { get; }

    public HttpStatusCode? StatusCode { get; }

    private static AgentApiFailureKind GetFailureKind(HttpStatusCode statusCode) =>
        statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                AgentApiFailureKind.Authentication,
            HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests =>
                AgentApiFailureKind.Transient,
            >= HttpStatusCode.InternalServerError => AgentApiFailureKind.Transient,
            _ => AgentApiFailureKind.Configuration,
        };
}

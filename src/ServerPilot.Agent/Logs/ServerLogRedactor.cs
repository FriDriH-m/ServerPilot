using System.Text.RegularExpressions;

namespace ServerPilot.Agent.Logs;

internal static partial class ServerLogRedactor
{
    public static string Redact(string content)
    {
        string withoutAuthorization = AuthorizationRegex().Replace(
            content,
            match => $"{match.Groups[1].Value}[REDACTED]");
        return SecretRegex().Replace(
            withoutAuthorization,
            match => $"{match.Groups[1].Value}{match.Groups[2].Value}[REDACTED]");
    }

    [GeneratedRegex(
        @"(?im)\b(authorization\s*[:=]\s*)[^\r\n]+",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex AuthorizationRegex();

    [GeneratedRegex(
        "(?im)\\b(password|passwd|pwd|token|secret|api[_ -]?key)(\\s*[:=]\\s*)(?:\"[^\"\\r\\n]*\"|'[^'\\r\\n]*'|[^\\s,;]+)",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex SecretRegex();
}

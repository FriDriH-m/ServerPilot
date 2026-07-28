namespace ServerPilot.Application.Authentication;

public static class EmailNormalizer
{
    public static string Canonicalize(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        return email.Trim();
    }

    public static string Normalize(string email) => Canonicalize(email).ToUpperInvariant();
}

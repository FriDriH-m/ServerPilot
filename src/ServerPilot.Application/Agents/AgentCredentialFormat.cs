namespace ServerPilot.Application.Agents;

public static class AgentCredentialFormat
{
    public const string Prefix = "spac_";
    public const int RandomHexLength = 64;
    public const int RawCredentialLength = 69;

    public static bool IsValid(string? rawCredential)
    {
        if (rawCredential is null ||
            rawCredential.Length != RawCredentialLength ||
            !rawCredential.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (char character in rawCredential.AsSpan(Prefix.Length))
        {
            if (!IsUppercaseHexadecimal(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsUppercaseHexadecimal(char character) =>
        character is (>= '0' and <= '9') or (>= 'A' and <= 'F');
}

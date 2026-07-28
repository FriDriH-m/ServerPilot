using ServerPilot.Application.Authentication;

namespace ServerPilot.UnitTests.Authentication;

public sealed class EmailNormalizerTests
{
    [Fact]
    public void CanonicalizeTrimsEmailWithoutChangingDisplayCasing()
    {
        string result = EmailNormalizer.Canonicalize("  User.Name@Example.COM  ");

        Assert.Equal("User.Name@Example.COM", result);
    }

    [Fact]
    public void NormalizeTrimsAndUsesInvariantUpperCase()
    {
        string result = EmailNormalizer.Normalize("  user.name@example.com  ");

        Assert.Equal("USER.NAME@EXAMPLE.COM", result);
    }
}

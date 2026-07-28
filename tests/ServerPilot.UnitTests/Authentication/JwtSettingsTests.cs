using ServerPilot.Infrastructure.Authentication;

namespace ServerPilot.UnitTests.Authentication;

public sealed class JwtSettingsTests
{
    [Fact]
    public void PublicExampleSigningKeyIsRejectedEvenThoughItIsLongEnough()
    {
        JwtSettings settings = CreateValidSettings(JwtSettings.UnsafeExampleSigningKey);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            settings.Validate);

        Assert.Contains("public example value", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RandomLookingSigningKeyWithSufficientLengthIsAccepted()
    {
        JwtSettings settings = CreateValidSettings(
            "f642557446234e9ba9c1cb35ad5e48e5f294334b741f4dd4");

        settings.Validate();
    }

    private static JwtSettings CreateValidSettings(string signingKey) => new()
    {
        Issuer = "ServerPilot.UnitTests",
        Audience = "ServerPilot.UnitTests.Client",
        SigningKey = signingKey,
    };
}

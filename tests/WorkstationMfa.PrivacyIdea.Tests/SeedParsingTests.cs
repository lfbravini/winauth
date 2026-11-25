using System.Text.Json;
using WorkstationMfa.PrivacyIdea;

namespace WorkstationMfa.PrivacyIdea.Tests;

public class SeedParsingTests
{
    [Fact]
    public void TryParseOtpAuthSecret_ExtractsBase32()
    {
        var url = "otpauth://totp/Label?secret=JBSWY3DPEHPK3PXP&issuer=Test";
        var secret = PrivacyIdeaClient.TryParseOtpAuthSecret(url);
        Assert.Equal("JBSWY3DPEHPK3PXP", secret);
    }

    [Fact]
    public void ExtractBase32Seed_FromGoogleUrl()
    {
        var json = """
        {
          "googleurl": { "value": "otpauth://totp/u@r?secret=MFZWIZLTOQ&period=30" },
          "otpkey": { "value": "seed://deadbeef" },
          "serial": "TOTP0001"
        }
        """;
        using var doc = JsonDocument.Parse(json);
        var seed = PrivacyIdeaClient.ExtractBase32Seed(doc.RootElement);
        Assert.Equal("MFZWIZLTOQ", seed);
    }

    [Fact]
    public void HexToBase32_RoundTripKnown()
    {
        // "Hello" = 48656c6c6f → Base32 JBSWY3DP
        var b32 = PrivacyIdeaClient.HexToBase32("48656c6c6f");
        Assert.Equal("JBSWY3DP", b32);
    }
}

using WorkstationMfa.Core.Totp;

namespace WorkstationMfa.Client.Tests;

public class TotpStoreTests
{
    [Fact]
    public void SaveLoad_RoundTrip_GeneratesCode()
    {
        var path = Path.Combine(Path.GetTempPath(), "wmfa-cred-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            var store = new TotpCredentialStore(path);
            store.Save(new TotpCredential
            {
                Upn = "user@example.com",
                DisplayName = "Horizon VDI (user@example.com)",
                Base32Secret = "JBSWY3DPEHPK3PXP",
                Digits = 6,
                Period = 30,
                Hash = "sha1"
            });

            var loaded = store.Load();
            Assert.NotNull(loaded);
            Assert.Equal("user@example.com", loaded!.Upn);
            var code = store.GenerateCode(loaded);
            Assert.Equal(6, code.Length);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

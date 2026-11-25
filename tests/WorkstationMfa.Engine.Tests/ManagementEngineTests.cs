using WorkstationMfa.Core.Models;
using WorkstationMfa.Engine;
using WorkstationMfa.Horizon;
using WorkstationMfa.PrivacyIdea;

namespace WorkstationMfa.Engine.Tests;

public class ManagementEngineTests
{
    [Fact]
    public void Classify_Unprovisioned_WhenNoTokens()
    {
        var status = ManagementEngine.Classify("alice", Array.Empty<PiTokenInfo>(), new LocalMfaStatus());
        Assert.Equal(UserProvisionStatus.Unprovisioned, status);
    }

    [Fact]
    public void Classify_NeedsRebind_WhenTokenButNoLocal()
    {
        var tokens = new[]
        {
            new PiTokenInfo { Serial = "TOTP1", Tokentype = "totp", Username = "alice" }
        };
        var status = ManagementEngine.Classify("alice", tokens, new LocalMfaStatus { IsProvisioned = false });
        Assert.Equal(UserProvisionStatus.NeedsRebind, status);
    }

    [Fact]
    public void Classify_Provisioned_WhenLocalMatches()
    {
        var tokens = new[]
        {
            new PiTokenInfo { Serial = "TOTP1", Tokentype = "totp", Username = "alice" }
        };
        var local = new LocalMfaStatus { IsProvisioned = true, BoundUpn = "alice@grupoalfarero.net" };
        var status = ManagementEngine.Classify("alice", tokens, local);
        Assert.Equal(UserProvisionStatus.Provisioned, status);
    }

    [Fact]
    public async Task Onboard_RequiresReplaceToken_WhenExisting()
    {
        var fake = new FakePi();
        fake.Tokens.Add(new PiTokenInfo { Serial = "OLD", Tokentype = "totp", Username = "bob" });
        var engine = new ManagementEngine(
            _ => fake,
            new FakeHorizon(),
            new FakeLocal());

        var result = await engine.OnboardAsync(new OnboardRequest
        {
            Profile = SampleProfile(),
            AdminUsername = "admin",
            AdminPassword = "x",
            TargetUsername = "bob",
            ReplaceToken = false,
            InstallHorizon = false,
            Verify = false
        });

        Assert.False(result.Success);
        Assert.Equal(ExitCode.ReplaceRequired, result.ExitCode);
    }

    [Fact]
    public async Task Onboard_Replace_DeletesAndEnrolls()
    {
        var fake = new FakePi();
        fake.Tokens.Add(new PiTokenInfo { Serial = "OLD", Tokentype = "totp", Username = "bob" });
        var local = new FakeLocal();
        var engine = new ManagementEngine(_ => fake, new FakeHorizon(), local);

        var result = await engine.OnboardAsync(new OnboardRequest
        {
            Profile = SampleProfile(),
            AdminUsername = "admin",
            AdminPassword = "x",
            TargetUsername = "bob",
            ReplaceToken = true,
            InstallHorizon = false,
            Verify = true
        });

        Assert.True(result.Success);
        Assert.Equal("OLD", result.OldSerial);
        Assert.Equal("NEW1", result.NewSerial);
        Assert.Contains("OLD", fake.Deleted);
        Assert.True(local.Provisioned);
    }

    private static ConnectionProfile SampleProfile() => new()
    {
        Id = "t",
        DisplayName = "T",
        PrivacyIdeaBaseUrl = "https://pi.example",
        Realm = "example.com"
    };

    private sealed class FakePi : IPrivacyIdeaClient
    {
        public List<PiTokenInfo> Tokens { get; } = new();
        public List<string> Deleted { get; } = new();

        public Task<AdminSession> AuthenticateAsync(string username, string password, string? realm = null, CancellationToken ct = default)
            => Task.FromResult(new AdminSession { Username = username, Token = "jwt", Realm = realm ?? "", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) });

        public Task<IReadOnlyList<PiUserInfo>> ListUsersAsync(AdminSession session, string realm, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PiUserInfo>>(Array.Empty<PiUserInfo>());

        public Task<IReadOnlyList<PiTokenInfo>> ListTokensAsync(AdminSession session, string realm, string? username = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PiTokenInfo>>(
                Tokens.Where(t => username is null || t.Username == username).ToList());

        public Task DeleteTokenAsync(AdminSession session, string serial, CancellationToken ct = default)
        {
            Deleted.Add(serial);
            Tokens.RemoveAll(t => t.Serial == serial);
            return Task.CompletedTask;
        }

        public Task<TotpEnrollment> InitTotpAsync(AdminSession session, string username, string realm, string description, int digits = 6, string hash = "sha1", int timestep = 30, CancellationToken ct = default)
            => Task.FromResult(new TotpEnrollment { Serial = "NEW1", Base32Seed = "JBSWY3DPEHPK3PXP", Description = description });

        public Task<bool> ValidateCheckAsync(string username, string realm, string otp, CancellationToken ct = default)
            => Task.FromResult(otp.Length == 6);
    }

    private sealed class FakeHorizon : IHorizonInstaller
    {
        public HorizonStatus GetStatus() => new() { State = HorizonInstallState.Installed, Version = "1.0" };
        public Task<HorizonStatus> InstallOrUpgradeAsync(HorizonInstallOptions options, CancellationToken ct = default)
            => Task.FromResult(GetStatus());
    }

    private sealed class FakeLocal : IMfaLocalProvisioner
    {
        public bool Provisioned { get; private set; }
        public LocalMfaStatus GetStatus() => new() { IsInstalled = true, IsProvisioned = Provisioned, BoundUpn = Provisioned ? "bob@example.com" : null };
        public Task ProvisionAsync(string upn, string base32Secret, CancellationToken ct = default)
        {
            Provisioned = true;
            return Task.CompletedTask;
        }
        public Task ClearAsync(CancellationToken ct = default)
        {
            Provisioned = false;
            return Task.CompletedTask;
        }
        public string GenerateCurrentCode() => "123456";
    }
}

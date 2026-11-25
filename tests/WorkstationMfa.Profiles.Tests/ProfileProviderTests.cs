using System.Text.Json;
using WorkstationMfa.Core.Models;
using WorkstationMfa.Profiles;

namespace WorkstationMfa.Profiles.Tests;

public class ProfileProviderTests
{
    [Fact]
    public async Task Resolve_ExplicitPath_Works()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wmfa-profile-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "test.json");
        var profile = new ConnectionProfile
        {
            Id = "test",
            DisplayName = "Test",
            PrivacyIdeaBaseUrl = "https://pi.example.com",
            Realm = "example.com"
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(profile, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));

        var provider = new ProfileProvider(programDataRoot: Path.Combine(dir, "pd"));
        var resolved = await provider.ResolveAsync(explicitPath: path);
        Assert.Equal(ProfileSourceKind.Explicit, resolved.SourceKind);
        Assert.Equal("test", resolved.Profile.Id);
        Assert.Equal("https://pi.example.com", resolved.Profile.PrivacyIdeaBaseUrl);
    }

    [Fact]
    public async Task Resolve_BundledGrupoAlfarero_WhenPresent()
    {
        var repoProfiles = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "packages", "profiles"));
        if (!Directory.Exists(repoProfiles))
        {
            // skip if layout differs in CI
            return;
        }

        var provider = new ProfileProvider(programDataRoot: Path.Combine(Path.GetTempPath(), "wmfa-empty-" + Guid.NewGuid().ToString("N")));
        var resolved = await provider.ResolveAsync(appBaseDirectory: Path.GetDirectoryName(repoProfiles)!);
        // FindBundled looks under appBase/profiles — point app base at packages
        var packagesRoot = Path.GetDirectoryName(repoProfiles)!;
        resolved = await provider.ResolveAsync(appBaseDirectory: packagesRoot);
        Assert.Equal("grupoalfarero", resolved.Profile.Id);
        Assert.Equal(ProfileSourceKind.Bundled, resolved.SourceKind);
    }
}

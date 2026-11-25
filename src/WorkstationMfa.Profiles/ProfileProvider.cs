using System.Text.Json;
using WorkstationMfa.Core.Models;

namespace WorkstationMfa.Profiles;

public interface IProfileProvider
{
    Task<ResolvedProfile> ResolveAsync(
        string? explicitPath = null,
        string? explicitUrl = null,
        string? appBaseDirectory = null,
        CancellationToken ct = default);

    Task SaveLocalAsync(ConnectionProfile profile, CancellationToken ct = default);
    IReadOnlyList<ConnectionProfile> ListLocalProfiles();
}

public sealed class ProfileProvider : IProfileProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly string _programDataRoot;

    public ProfileProvider(HttpClient? http = null, string? programDataRoot = null)
    {
        _http = http ?? new HttpClient();
        _programDataRoot = programDataRoot
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WorkstationMfa");
    }

    public async Task<ResolvedProfile> ResolveAsync(
        string? explicitPath = null,
        string? explicitUrl = null,
        string? appBaseDirectory = null,
        CancellationToken ct = default)
    {
        appBaseDirectory ??= AppContext.BaseDirectory;

        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var profile = await LoadFromFileAsync(explicitPath, ct).ConfigureAwait(false);
            return new ResolvedProfile
            {
                Profile = profile,
                SourceKind = ProfileSourceKind.Explicit,
                SourcePathOrUrl = explicitPath
            };
        }

        if (!string.IsNullOrWhiteSpace(explicitUrl))
        {
            try
            {
                var profile = await LoadFromUrlOrUncAsync(explicitUrl, ct).ConfigureAwait(false);
                await CacheExternalAsync(profile, ct).ConfigureAwait(false);
                return new ResolvedProfile
                {
                    Profile = profile,
                    SourceKind = ProfileSourceKind.External,
                    SourcePathOrUrl = explicitUrl
                };
            }
            catch (Exception ex)
            {
                var fallback = TryLoadCachedOrBundled(appBaseDirectory, out var kind, out var path);
                if (fallback is not null)
                {
                    return new ResolvedProfile
                    {
                        Profile = fallback,
                        SourceKind = kind,
                        SourcePathOrUrl = path,
                        Warning = $"External profile failed ({ex.Message}); fell back to {kind}."
                    };
                }
                throw;
            }
        }

        var pointerPath = Path.Combine(_programDataRoot, "profile.source");
        if (File.Exists(pointerPath))
        {
            try
            {
                var pointerJson = await File.ReadAllTextAsync(pointerPath, ct).ConfigureAwait(false);
                var pointer = JsonSerializer.Deserialize<ProfileSourcePointer>(pointerJson, JsonOptions);
                if (!string.IsNullOrWhiteSpace(pointer?.ProfileUrl))
                {
                    var profile = await LoadFromUrlOrUncAsync(pointer.ProfileUrl, ct).ConfigureAwait(false);
                    await CacheExternalAsync(profile, ct).ConfigureAwait(false);
                    return new ResolvedProfile
                    {
                        Profile = profile,
                        SourceKind = ProfileSourceKind.External,
                        SourcePathOrUrl = pointer.ProfileUrl
                    };
                }
            }
            catch (Exception ex)
            {
                var fallback = TryLoadCachedOrBundled(appBaseDirectory, out var kind, out var path);
                if (fallback is not null)
                {
                    return new ResolvedProfile
                    {
                        Profile = fallback,
                        SourceKind = kind,
                        SourcePathOrUrl = path,
                        Warning = $"Managed profile.source failed ({ex.Message}); fell back to {kind}."
                    };
                }
            }
        }

        var localDefault = Path.Combine(_programDataRoot, "profiles");
        if (Directory.Exists(localDefault))
        {
            var first = Directory.GetFiles(localDefault, "*.json").OrderBy(f => f).FirstOrDefault();
            if (first is not null)
            {
                var profile = await LoadFromFileAsync(first, ct).ConfigureAwait(false);
                return new ResolvedProfile
                {
                    Profile = profile,
                    SourceKind = ProfileSourceKind.Local,
                    SourcePathOrUrl = first
                };
            }
        }

        var bundled = FindBundledProfile(appBaseDirectory);
        if (bundled is not null)
        {
            var profile = await LoadFromFileAsync(bundled, ct).ConfigureAwait(false);
            return new ResolvedProfile
            {
                Profile = profile,
                SourceKind = ProfileSourceKind.Bundled,
                SourcePathOrUrl = bundled
            };
        }

        throw new InvalidOperationException("No connection profile found (bundled, local, or external).");
    }

    public async Task SaveLocalAsync(ConnectionProfile profile, CancellationToken ct = default)
    {
        var dir = Path.Combine(_programDataRoot, "profiles");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{SanitizeFileName(profile.Id)}.json");
        var json = JsonSerializer.Serialize(profile, JsonOptions);
        await File.WriteAllTextAsync(path, json, ct).ConfigureAwait(false);
    }

    public IReadOnlyList<ConnectionProfile> ListLocalProfiles()
    {
        var dir = Path.Combine(_programDataRoot, "profiles");
        if (!Directory.Exists(dir))
            return Array.Empty<ConnectionProfile>();

        var list = new List<ConnectionProfile>();
        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var profile = JsonSerializer.Deserialize<ConnectionProfile>(json, JsonOptions);
                if (profile is not null)
                    list.Add(profile);
            }
            catch
            {
                // skip bad files
            }
        }
        return list;
    }

    private async Task<ConnectionProfile> LoadFromFileAsync(string path, CancellationToken ct)
    {
        var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        var profile = JsonSerializer.Deserialize<ConnectionProfile>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Invalid profile JSON: {path}");
        Validate(profile);
        return profile;
    }

    private async Task<ConnectionProfile> LoadFromUrlOrUncAsync(string urlOrUnc, CancellationToken ct)
    {
        if (urlOrUnc.StartsWith(@"\\", StringComparison.Ordinal) || File.Exists(urlOrUnc))
            return await LoadFromFileAsync(urlOrUnc, ct).ConfigureAwait(false);

        using var response = await _http.GetAsync(urlOrUnc, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var profile = JsonSerializer.Deserialize<ConnectionProfile>(json, JsonOptions)
            ?? throw new InvalidOperationException("Invalid external profile JSON.");
        Validate(profile);
        return profile;
    }

    private async Task CacheExternalAsync(ConnectionProfile profile, CancellationToken ct)
    {
        var cacheDir = Path.Combine(_programDataRoot, "cache");
        Directory.CreateDirectory(cacheDir);
        var path = Path.Combine(cacheDir, "last-good-profile.json");
        var json = JsonSerializer.Serialize(profile, JsonOptions);
        await File.WriteAllTextAsync(path, json, ct).ConfigureAwait(false);
    }

    private ConnectionProfile? TryLoadCachedOrBundled(string appBase, out ProfileSourceKind kind, out string path)
    {
        var cache = Path.Combine(_programDataRoot, "cache", "last-good-profile.json");
        if (File.Exists(cache))
        {
            try
            {
                var json = File.ReadAllText(cache);
                var profile = JsonSerializer.Deserialize<ConnectionProfile>(json, JsonOptions);
                if (profile is not null)
                {
                    kind = ProfileSourceKind.Local;
                    path = cache;
                    return profile;
                }
            }
            catch { /* ignore */ }
        }

        var bundled = FindBundledProfile(appBase);
        if (bundled is not null)
        {
            try
            {
                var json = File.ReadAllText(bundled);
                var profile = JsonSerializer.Deserialize<ConnectionProfile>(json, JsonOptions);
                if (profile is not null)
                {
                    kind = ProfileSourceKind.Bundled;
                    path = bundled;
                    return profile;
                }
            }
            catch { /* ignore */ }
        }

        kind = ProfileSourceKind.Bundled;
        path = string.Empty;
        return null;
    }

    private static string? FindBundledProfile(string appBase)
    {
        var candidates = new[]
        {
            Path.Combine(appBase, "profiles"),
            Path.GetFullPath(Path.Combine(appBase, "..", "..", "..", "..", "..", "packages", "profiles")),
            Path.GetFullPath(Path.Combine(appBase, "..", "..", "..", "..", "packages", "profiles")),
            Path.Combine(Directory.GetCurrentDirectory(), "packages", "profiles")
        };

        foreach (var dir in candidates.Distinct())
        {
            if (!Directory.Exists(dir))
                continue;
            var file = Directory.GetFiles(dir, "*.json").OrderBy(f => f).FirstOrDefault();
            if (file is not null)
                return file;
        }
        return null;
    }

    private static void Validate(ConnectionProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Id))
            throw new InvalidOperationException("Profile id is required.");
        if (string.IsNullOrWhiteSpace(profile.PrivacyIdeaBaseUrl))
            throw new InvalidOperationException("Profile privacyIdeaBaseUrl is required.");
        if (string.IsNullOrWhiteSpace(profile.Realm))
            throw new InvalidOperationException("Profile realm is required.");
    }

    private static string SanitizeFileName(string id)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            id = id.Replace(c, '_');
        return id;
    }
}

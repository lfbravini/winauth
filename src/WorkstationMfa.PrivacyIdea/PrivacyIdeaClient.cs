using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WorkstationMfa.Core.Models;

namespace WorkstationMfa.PrivacyIdea;

public sealed class PrivacyIdeaClient : IPrivacyIdeaClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public PrivacyIdeaClient(string baseUrl, bool validateTls = true, HttpMessageHandler? handler = null)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ArgumentException("Base URL is required.", nameof(baseUrl));

        var root = baseUrl.TrimEnd('/') + "/";
        if (handler is null)
        {
            var sockets = new HttpClientHandler();
            if (!validateTls)
            {
                sockets.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            }
            _http = new HttpClient(sockets) { BaseAddress = new Uri(root) };
            _ownsHttp = true;
        }
        else
        {
            _http = new HttpClient(handler) { BaseAddress = new Uri(root) };
            _ownsHttp = true;
        }
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<AdminSession> AuthenticateAsync(string username, string password, string? realm = null, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["username"] = username,
            ["password"] = password
        };
        if (!string.IsNullOrWhiteSpace(realm))
            form["realm"] = realm;

        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync("auth", content, ct).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        if (!TryGetResultValue(doc.RootElement, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new PrivacyIdeaException("Authentication failed.", json);

        if (!value.TryGetProperty("token", out var tokenEl))
            throw new PrivacyIdeaException("Authentication response missing token.", Sanitize(json));

        var token = tokenEl.GetString() ?? throw new PrivacyIdeaException("Empty auth token.");
        var role = value.TryGetProperty("role", out var roleEl) ? roleEl.GetString() ?? "admin" : "admin";
        var user = value.TryGetProperty("username", out var userEl) ? userEl.GetString() ?? username : username;
        var realmOut = value.TryGetProperty("realm", out var realmEl) ? realmEl.GetString() ?? realm ?? string.Empty : realm ?? string.Empty;

        // Default JWT validity ~1 hour if not parseable
        var expires = DateTimeOffset.UtcNow.AddHours(1);
        return new AdminSession
        {
            Username = user,
            Token = token,
            Realm = realmOut,
            Role = role,
            ExpiresAt = expires
        };
    }

    public async Task<IReadOnlyList<PiUserInfo>> ListUsersAsync(AdminSession session, string realm, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, $"user/?realm={Uri.EscapeDataString(realm)}", session);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        if (!TryGetResultValue(doc.RootElement, out var value))
            throw new PrivacyIdeaException("Failed to list users.", Sanitize(json));

        var list = new List<PiUserInfo>();
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                list.Add(ParseUser(item, realm));
            }
        }
        else if (value.ValueKind == JsonValueKind.Object)
        {
            // Some PI versions return a dict keyed by username
            foreach (var prop in value.EnumerateObject())
            {
                list.Add(ParseUser(prop.Value, realm, prop.Name));
            }
        }

        return list;
    }

    public async Task<IReadOnlyList<PiTokenInfo>> ListTokensAsync(AdminSession session, string realm, string? username = null, CancellationToken ct = default)
    {
        var qs = $"token/?realm={Uri.EscapeDataString(realm)}";
        if (!string.IsNullOrWhiteSpace(username))
            qs += $"&user={Uri.EscapeDataString(username)}";

        using var request = CreateAuthorizedRequest(HttpMethod.Get, qs, session);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        if (!TryGetResultValue(doc.RootElement, out var value))
            throw new PrivacyIdeaException("Failed to list tokens.", Sanitize(json));

        var list = new List<PiTokenInfo>();
        JsonElement tokensEl = value;
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("tokens", out var nested))
            tokensEl = nested;

        if (tokensEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in tokensEl.EnumerateArray())
            {
                list.Add(ParseToken(item, realm));
            }
        }

        return list;
    }

    public async Task DeleteTokenAsync(AdminSession session, string serial, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Delete, $"token/{Uri.EscapeDataString(serial)}", session);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        if (!IsResultTrue(doc.RootElement))
            throw new PrivacyIdeaException($"Failed to delete token {serial}.", Sanitize(json));
    }

    public async Task<TotpEnrollment> InitTotpAsync(
        AdminSession session,
        string username,
        string realm,
        string description,
        int digits = 6,
        string hash = "sha1",
        int timestep = 30,
        CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["type"] = "totp",
            ["genkey"] = "1",
            ["otplen"] = digits.ToString(),
            ["hashlib"] = hash,
            ["timestep"] = timestep.ToString(),
            ["user"] = username,
            ["realm"] = realm,
            ["description"] = description
        };

        using var content = new FormUrlEncodedContent(form);
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "token/init", session);
        request.Content = content;
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("detail", out var detail))
            throw new PrivacyIdeaException("token/init missing detail.", Sanitize(json));

        var serial = detail.TryGetProperty("serial", out var serialEl) ? serialEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(serial))
            throw new PrivacyIdeaException("token/init missing serial.", Sanitize(json));

        var seed = ExtractBase32Seed(detail);
        if (string.IsNullOrWhiteSpace(seed))
            throw new PrivacyIdeaException("token/init missing TOTP seed.", Sanitize(json));

        return new TotpEnrollment
        {
            Serial = serial!,
            Base32Seed = seed!,
            Description = description
        };
    }

    public async Task<bool> ValidateCheckAsync(string username, string realm, string otp, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["user"] = username,
            ["realm"] = realm,
            ["pass"] = otp
        };
        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync("validate/check", content, ct).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        return IsResultTrue(doc.RootElement);
    }

    public void Dispose()
    {
        if (_ownsHttp)
            _http.Dispose();
    }

    private HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string relativeUrl, AdminSession session)
    {
        var request = new HttpRequestMessage(method, relativeUrl);
        request.Headers.TryAddWithoutValidation("PI-Authorization", session.Token);
        request.Headers.TryAddWithoutValidation("Authorization", session.Token);
        return request;
    }

    private static bool TryGetResultValue(JsonElement root, out JsonElement value)
    {
        value = default;
        if (!root.TryGetProperty("result", out var result))
            return false;
        if (result.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.False)
            return false;
        if (!result.TryGetProperty("value", out value))
            return false;
        return true;
    }

    private static bool IsResultTrue(JsonElement root)
    {
        if (!root.TryGetProperty("result", out var result))
            return false;
        if (result.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.False)
            return false;
        if (!result.TryGetProperty("value", out var value))
            return false;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Object when value.TryGetProperty("value", out var nested) => nested.ValueKind == JsonValueKind.True,
            _ => false
        };
    }

    private static PiUserInfo ParseUser(JsonElement item, string realm, string? usernameFallback = null)
    {
        string? username = null;
        if (item.TryGetProperty("username", out var u)) username = u.GetString();
        if (username is null && item.TryGetProperty("user", out var u2)) username = u2.GetString();
        username ??= usernameFallback ?? string.Empty;

        return new PiUserInfo
        {
            Username = username,
            Email = item.TryGetProperty("email", out var e) ? e.GetString() : null,
            GivenName = item.TryGetProperty("givenname", out var g) ? g.GetString() : null,
            Surname = item.TryGetProperty("surname", out var s) ? s.GetString() : null,
            Realm = realm
        };
    }

    private static PiTokenInfo ParseToken(JsonElement item, string realm)
    {
        var serial = item.TryGetProperty("serial", out var s) ? s.GetString() ?? string.Empty : string.Empty;
        var type = item.TryGetProperty("tokentype", out var t) ? t.GetString() ?? "totp" : "totp";
        var active = true;
        if (item.TryGetProperty("active", out var a))
        {
            active = a.ValueKind == JsonValueKind.True || (a.ValueKind == JsonValueKind.String && a.GetString() == "True");
        }

        string? username = null;
        if (item.TryGetProperty("username", out var u)) username = u.GetString();
        else if (item.TryGetProperty("user_id", out var uid)) username = uid.GetString();
        else if (item.TryGetProperty("user", out var userObj) && userObj.ValueKind == JsonValueKind.Object
                 && userObj.TryGetProperty("user", out var nestedUser))
            username = nestedUser.GetString();

        return new PiTokenInfo
        {
            Serial = serial,
            Tokentype = type ?? "totp",
            Active = active,
            Description = item.TryGetProperty("description", out var d) ? d.GetString() : null,
            Username = username,
            Realm = realm
        };
    }

    public static string? ExtractBase32Seed(JsonElement detail)
    {
        // Preferred: otpauth URL secret=
        if (detail.TryGetProperty("googleurl", out var google) &&
            google.TryGetProperty("value", out var otpauthEl))
        {
            var otpauth = otpauthEl.GetString();
            var secret = TryParseOtpAuthSecret(otpauth);
            if (!string.IsNullOrWhiteSpace(secret))
                return secret;
        }

        // Fallback: seed://HEX → Base32
        if (detail.TryGetProperty("otpkey", out var otpkey) &&
            otpkey.TryGetProperty("value", out var keyEl))
        {
            var raw = keyEl.GetString();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                var hex = raw.StartsWith("seed://", StringComparison.OrdinalIgnoreCase)
                    ? raw["seed://".Length..]
                    : raw;
                return HexToBase32(hex);
            }
        }

        return null;
    }

    public static string? TryParseOtpAuthSecret(string? otpauth)
    {
        if (string.IsNullOrWhiteSpace(otpauth))
            return null;
        var qIndex = otpauth.IndexOf('?', StringComparison.Ordinal);
        if (qIndex < 0)
            return null;
        var query = otpauth[(qIndex + 1)..];
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0].Equals("secret", StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(kv[1]).Replace(" ", "", StringComparison.Ordinal);
        }
        return null;
    }

    public static string HexToBase32(string hex)
    {
        hex = hex.Trim();
        if (hex.Length % 2 != 0)
            throw new FormatException("Invalid hex seed length.");

        var bytes = Convert.FromHexString(hex);
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new StringBuilder((bytes.Length * 8 + 4) / 5);
        int buffer = 0, bitsLeft = 0;
        foreach (var b in bytes)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                output.Append(alphabet[(buffer >> (bitsLeft - 5)) & 31]);
                bitsLeft -= 5;
            }
        }
        if (bitsLeft > 0)
            output.Append(alphabet[(buffer << (5 - bitsLeft)) & 31]);
        return output.ToString();
    }

    private static string Sanitize(string json)
    {
        // Strip likely seed material from error payloads before surfacing
        return json
            .Replace("otpauth://", "otpauth://[redacted]", StringComparison.OrdinalIgnoreCase)
            .Replace("seed://", "seed://[redacted]", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class PrivacyIdeaException : Exception
{
    public string? ResponseBody { get; }

    public PrivacyIdeaException(string message, string? responseBody = null)
        : base(message)
    {
        ResponseBody = responseBody;
    }
}

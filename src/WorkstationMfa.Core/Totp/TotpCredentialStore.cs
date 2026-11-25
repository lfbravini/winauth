using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OtpNet;

namespace WorkstationMfa.Core.Totp;

public sealed class TotpCredential
{
    public string Upn { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Base32Secret { get; set; } = string.Empty;
    public int Digits { get; set; } = 6;
    public int Period { get; set; } = 30;
    public string Hash { get; set; } = "sha1";
}

/// <summary>
/// DPAPI Machine+User bound store for a single workstation TOTP credential.
/// </summary>
public sealed class TotpCredentialStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("WorkstationMfa.v1.MachineUser");
    private readonly string _filePath;

    public TotpCredentialStore(string? filePath = null)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WorkstationMfa");
        Directory.CreateDirectory(dir);
        _filePath = filePath ?? Path.Combine(dir, "credential.bin");
    }

    public string StorePath => _filePath;

    public bool Exists => File.Exists(_filePath);

    public void Save(TotpCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        if (string.IsNullOrWhiteSpace(credential.Base32Secret))
            throw new ArgumentException("Secret is required.", nameof(credential));

        var json = JsonSerializer.Serialize(credential);
        var plain = Encoding.UTF8.GetBytes(json);
        try
        {
            // CurrentUser scope + app entropy approximates Machine|User binding for this product.
            // ProtectedData on Windows with CurrentUser is user-bound; we also embed machine name.
            var payload = new StorePayload
            {
                MachineName = Environment.MachineName,
                UserName = Environment.UserName,
                Data = plain
            };
            var envelope = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
            var protectedBytes = ProtectedData.Protect(envelope, Entropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(_filePath, protectedBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public TotpCredential? Load()
    {
        if (!File.Exists(_filePath))
            return null;

        var protectedBytes = File.ReadAllBytes(_filePath);
        byte[] envelope;
        try
        {
            envelope = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("Credential store could not be decrypted on this machine/user (DPAPI binding).");
        }

        var payload = JsonSerializer.Deserialize<StorePayload>(Encoding.UTF8.GetString(envelope))
            ?? throw new InvalidOperationException("Corrupt credential store.");

        if (!string.Equals(payload.MachineName, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Credential store is bound to a different machine.");

        var credential = JsonSerializer.Deserialize<TotpCredential>(Encoding.UTF8.GetString(payload.Data))
            ?? throw new InvalidOperationException("Corrupt credential payload.");
        return credential;
    }

    public void Clear()
    {
        if (File.Exists(_filePath))
            File.Delete(_filePath);
    }

    public string GenerateCode(TotpCredential? credential = null)
    {
        credential ??= Load() ?? throw new InvalidOperationException("No local credential provisioned.");
        var key = Base32Encoding.ToBytes(credential.Base32Secret);
        var mode = credential.Hash.ToLowerInvariant() switch
        {
            "sha256" => OtpHashMode.Sha256,
            "sha512" => OtpHashMode.Sha512,
            _ => OtpHashMode.Sha1
        };
        var totp = new OtpNet.Totp(key, step: credential.Period, mode: mode, totpSize: credential.Digits);
        return totp.ComputeTotp();
    }

    private sealed class StorePayload
    {
        public string MachineName { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public byte[] Data { get; set; } = Array.Empty<byte>();
    }
}

using WorkstationMfa.Core.Totp;

namespace WorkstationMfa.Engine;

public sealed class LocalMfaProvisioner : IMfaLocalProvisioner
{
    private readonly TotpCredentialStore _store;
    private readonly string? _clientExePath;

    public LocalMfaProvisioner(TotpCredentialStore? store = null, string? clientExePath = null)
    {
        _store = store ?? new TotpCredentialStore();
        _clientExePath = clientExePath ?? FindClientExe();
    }

    public LocalMfaStatus GetStatus()
    {
        string? upn = null;
        var provisioned = false;
        try
        {
            if (_store.Exists)
            {
                var cred = _store.Load();
                if (cred is not null)
                {
                    provisioned = true;
                    upn = cred.Upn;
                }
            }
        }
        catch
        {
            provisioned = false;
        }

        return new LocalMfaStatus
        {
            IsInstalled = !string.IsNullOrWhiteSpace(_clientExePath) && File.Exists(_clientExePath),
            IsProvisioned = provisioned,
            BoundUpn = upn,
            ClientPath = _clientExePath
        };
    }

    public Task ProvisionAsync(string upn, string base32Secret, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var credential = new TotpCredential
        {
            Upn = upn,
            DisplayName = $"Horizon VDI ({upn})",
            Base32Secret = base32Secret,
            Digits = 6,
            Period = 30,
            Hash = "sha1"
        };
        _store.Save(credential);

        // Prefer invoking client silent path when available (ensures startup shortcuts etc.)
        if (!string.IsNullOrWhiteSpace(_clientExePath) && File.Exists(_clientExePath))
        {
            // Store already written; client silent provision would rewrite — call for prefs only via args without re-passing secret if we add that later.
            // For now store is source of truth shared with client.
        }

        TryEnableStartWithWindows();
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _store.Clear();
        return Task.CompletedTask;
    }

    public string GenerateCurrentCode() => _store.GenerateCode();

    private static string? FindClientExe()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "WorkstationMfa.Client.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "WorkstationMfa.Client", "WorkstationMfa.Client.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WorkstationMfa", "WorkstationMfa.Client.exe")
        };
        return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
    }

    private static void TryEnableStartWithWindows()
    {
        try
        {
            var client = FindClientExe();
            if (client is null)
                return;
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            key?.SetValue("WorkstationMfa", $"\"{client}\"");
        }
        catch
        {
            // non-fatal
        }
    }
}

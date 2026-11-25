namespace WorkstationMfa.Core.Models;

public sealed class ConnectionProfile
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PrivacyIdeaBaseUrl { get; set; } = string.Empty;
    public string Realm { get; set; } = string.Empty;
    public bool ValidateTls { get; set; } = true;
    public string? HorizonBroker { get; set; }
    public string? HorizonMediaRelativePath { get; set; }
    public TotpSettings Totp { get; set; } = new();
}

public sealed class TotpSettings
{
    public int Digits { get; set; } = 6;
    public string Hash { get; set; } = "sha1";
    public int Period { get; set; } = 30;
}

public sealed class ProfileSourcePointer
{
    public string? ProfileUrl { get; set; }
    public int RefreshHours { get; set; } = 24;
}

public enum ProfileSourceKind
{
    Bundled,
    Local,
    External,
    Explicit
}

public sealed class ResolvedProfile
{
    public required ConnectionProfile Profile { get; init; }
    public required ProfileSourceKind SourceKind { get; init; }
    public required string SourcePathOrUrl { get; init; }
    public string? Warning { get; init; }
}

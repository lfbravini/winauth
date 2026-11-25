namespace WorkstationMfa.Engine;

public sealed class LocalMfaStatus
{
    public bool IsInstalled { get; init; }
    public bool IsProvisioned { get; init; }
    public string? BoundUpn { get; init; }
    public string? ClientPath { get; init; }
}

public interface IMfaLocalProvisioner
{
    LocalMfaStatus GetStatus();
    Task ProvisionAsync(string upn, string base32Secret, CancellationToken ct = default);
    Task ClearAsync(CancellationToken ct = default);
    string GenerateCurrentCode();
}

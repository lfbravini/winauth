namespace WorkstationMfa.Horizon;

public enum HorizonInstallState
{
    NotInstalled,
    Installed,
    Unknown
}

public sealed class HorizonStatus
{
    public HorizonInstallState State { get; init; }
    public string? Version { get; init; }
    public string? InstallPath { get; init; }
    public string? DisplayName { get; init; }
}

public sealed class HorizonInstallOptions
{
    public required string MediaPath { get; init; }
    public string? Broker { get; init; }
    public bool WhatIf { get; init; }
    public bool AllowUpgrade { get; init; } = true;
}

public interface IHorizonInstaller
{
    HorizonStatus GetStatus();
    Task<HorizonStatus> InstallOrUpgradeAsync(HorizonInstallOptions options, CancellationToken ct = default);
}

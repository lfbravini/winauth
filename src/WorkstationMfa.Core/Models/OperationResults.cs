namespace WorkstationMfa.Core.Models;

public enum ExitCode
{
    Success = 0,
    GeneralFailure = 1,
    AuthFailed = 10,
    ProfileFailed = 20,
    HorizonFailed = 30,
    EnrollFailed = 40,
    BindFailed = 50,
    VerifyFailed = 60,
    ReplaceRequired = 70,
    Cancelled = 80
}

public enum UserProvisionStatus
{
    Unprovisioned,
    Provisioned,
    NeedsRebind,
    ReplaceRequired
}

public sealed class OperationResult
{
    public bool Success { get; init; }
    public ExitCode ExitCode { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? User { get; init; }
    public string? OldSerial { get; init; }
    public string? NewSerial { get; init; }
    public string? Hostname { get; init; }
    public string? HorizonVersion { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public bool WhatIf { get; init; }
    public IReadOnlyList<string> Actions { get; init; } = Array.Empty<string>();

    public static OperationResult Ok(string message, ExitCode code = ExitCode.Success) =>
        new() { Success = true, ExitCode = code, Message = message };

    public static OperationResult Fail(ExitCode code, string message) =>
        new() { Success = false, ExitCode = code, Message = message };
}

public sealed class AdminSession
{
    public required string Username { get; init; }
    public required string Token { get; init; }
    public required string Realm { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public string Role { get; init; } = "admin";

    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
}

public sealed class PiUserInfo
{
    public required string Username { get; init; }
    public string? Email { get; init; }
    public string? GivenName { get; init; }
    public string? Surname { get; init; }
    public string Realm { get; init; } = string.Empty;
}

public sealed class PiTokenInfo
{
    public required string Serial { get; init; }
    public string Tokentype { get; init; } = "totp";
    public bool Active { get; init; } = true;
    public string? Description { get; init; }
    public string? Username { get; init; }
    public string? Realm { get; init; }
}

public sealed class TotpEnrollment
{
    public required string Serial { get; init; }
    public required string Base32Seed { get; init; }
    public string? Description { get; init; }
}

public sealed class UserTokenStatus
{
    public required PiUserInfo User { get; init; }
    public required UserProvisionStatus Status { get; init; }
    public IReadOnlyList<PiTokenInfo> Tokens { get; init; } = Array.Empty<PiTokenInfo>();
    public string? LocalBoundUpn { get; init; }
}

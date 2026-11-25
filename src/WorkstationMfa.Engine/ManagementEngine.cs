using WorkstationMfa.Core.Models;
using WorkstationMfa.Core.Security;
using WorkstationMfa.Core.Totp;
using WorkstationMfa.Horizon;
using WorkstationMfa.PrivacyIdea;
using WorkstationMfa.Profiles;

namespace WorkstationMfa.Engine;

public sealed class OnboardRequest
{
    public required ConnectionProfile Profile { get; init; }
    public required string AdminUsername { get; init; }
    public required string AdminPassword { get; init; }
    public required string TargetUsername { get; init; }
    public bool InstallHorizon { get; init; } = true;
    public bool ReplaceToken { get; init; }
    public bool Verify { get; init; } = true;
    public bool WhatIf { get; init; }
    public string? HorizonMediaPath { get; init; }
    public string? AppBaseDirectory { get; init; }
}

public sealed class ManagementEngine
{
    private readonly Func<ConnectionProfile, IPrivacyIdeaClient> _piFactory;
    private readonly IHorizonInstaller _horizon;
    private readonly IMfaLocalProvisioner _local;
    private readonly IProfileProvider _profiles;

    public ManagementEngine(
        Func<ConnectionProfile, IPrivacyIdeaClient>? piFactory = null,
        IHorizonInstaller? horizon = null,
        IMfaLocalProvisioner? local = null,
        IProfileProvider? profiles = null)
    {
        _piFactory = piFactory ?? (p => new PrivacyIdeaClient(p.PrivacyIdeaBaseUrl, p.ValidateTls));
        _horizon = horizon ?? new HorizonInstaller();
        _local = local ?? new LocalMfaProvisioner();
        _profiles = profiles ?? new ProfileProvider();
    }

    public IProfileProvider Profiles => _profiles;
    public IHorizonInstaller Horizon => _horizon;
    public IMfaLocalProvisioner Local => _local;

    public async Task<AdminSession> AuthenticateAsync(ConnectionProfile profile, string username, string password, CancellationToken ct = default)
    {
        var client = _piFactory(profile);
        try
        {
            return await client.AuthenticateAsync(username, password, profile.Realm, ct).ConfigureAwait(false);
        }
        finally
        {
            (client as IDisposable)?.Dispose();
        }
    }

    public async Task<IReadOnlyList<UserTokenStatus>> ListUserStatusesAsync(
        ConnectionProfile profile,
        AdminSession session,
        CancellationToken ct = default)
    {
        var client = _piFactory(profile);
        try
        {
            var users = await client.ListUsersAsync(session, profile.Realm, ct).ConfigureAwait(false);
            var tokens = await client.ListTokensAsync(session, profile.Realm, ct: ct).ConfigureAwait(false);
            var local = _local.GetStatus();

            return users.Select(u =>
            {
                var userTokens = tokens.Where(t =>
                    string.Equals(t.Username, u.Username, StringComparison.OrdinalIgnoreCase)).ToList();
                var status = Classify(u.Username, userTokens, local);
                return new UserTokenStatus
                {
                    User = u,
                    Status = status,
                    Tokens = userTokens,
                    LocalBoundUpn = local.BoundUpn
                };
            }).OrderBy(x => x.User.Username).ToList();
        }
        finally
        {
            (client as IDisposable)?.Dispose();
        }
    }

    public async Task<OperationResult> OnboardAsync(OnboardRequest request, CancellationToken ct = default)
    {
        var actions = new List<string>();
        var hostname = Environment.MachineName;
        string? oldSerial = null;
        string? newSerial = null;
        string? horizonVersion = null;

        try
        {
            var client = _piFactory(request.Profile);
            try
            {
                var session = await client.AuthenticateAsync(request.AdminUsername, request.AdminPassword, request.Profile.Realm, ct)
                    .ConfigureAwait(false);
                actions.Add($"Authenticated as {session.Username}");

                var existing = await client.ListTokensAsync(session, request.Profile.Realm, request.TargetUsername, ct)
                    .ConfigureAwait(false);
                var toReplace = TokenPolicy.SelectTokensToReplace(existing);

                if (toReplace.Count > 0 && !request.ReplaceToken && !request.WhatIf)
                {
                    return new OperationResult
                    {
                        Success = false,
                        ExitCode = ExitCode.ReplaceRequired,
                        Message = $"User already has {toReplace.Count} TOTP token(s). Pass --replace-token to destroy and re-enroll.",
                        User = request.TargetUsername,
                        OldSerial = string.Join(",", toReplace.Select(t => t.Serial)),
                        Hostname = hostname,
                        Actions = actions
                    };
                }

                if (request.InstallHorizon)
                {
                    var media = ResolveHorizonMedia(request);
                    var hz = _horizon.GetStatus();
                    if (hz.State == HorizonInstallState.NotInstalled || request.ReplaceToken)
                    {
                        actions.Add(hz.State == HorizonInstallState.NotInstalled
                            ? $"Install Horizon from {media}"
                            : $"Ensure Horizon (current {hz.Version})");
                        if (!request.WhatIf)
                        {
                            if (!File.Exists(media))
                                return OperationResult.Fail(ExitCode.HorizonFailed, $"Horizon media not found: {media}");

                            hz = await _horizon.InstallOrUpgradeAsync(new HorizonInstallOptions
                            {
                                MediaPath = media,
                                Broker = request.Profile.HorizonBroker,
                                WhatIf = false
                            }, ct).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        actions.Add($"Horizon already installed ({hz.Version})");
                    }
                    horizonVersion = hz.Version;
                }

                if (toReplace.Count > 0)
                {
                    oldSerial = string.Join(",", toReplace.Select(t => t.Serial));
                    foreach (var token in toReplace)
                    {
                        actions.Add($"Delete token {token.Serial}");
                        if (!request.WhatIf)
                            await client.DeleteTokenAsync(session, token.Serial, ct).ConfigureAwait(false);
                    }
                }

                var description = TokenPolicy.BuildDescription(hostname, DateTimeOffset.UtcNow);
                actions.Add($"Enroll TOTP for {request.TargetUsername}@{request.Profile.Realm}");

                string? seed = null;
                if (!request.WhatIf)
                {
                    var enrollment = await client.InitTotpAsync(
                        session,
                        request.TargetUsername,
                        request.Profile.Realm,
                        description,
                        request.Profile.Totp.Digits,
                        request.Profile.Totp.Hash,
                        request.Profile.Totp.Period,
                        ct).ConfigureAwait(false);
                    newSerial = enrollment.Serial;
                    seed = enrollment.Base32Seed;

                    var upn = BuildUpn(request.TargetUsername, request.Profile.Realm);
                    actions.Add($"Bind local MFA for {upn}");
                    try
                    {
                        await _local.ProvisionAsync(upn, seed, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        return new OperationResult
                        {
                            Success = false,
                            ExitCode = ExitCode.BindFailed,
                            Message = $"Local bind failed: {ex.Message}",
                            User = request.TargetUsername,
                            OldSerial = oldSerial,
                            NewSerial = newSerial,
                            Hostname = hostname,
                            HorizonVersion = horizonVersion,
                            Actions = actions
                        };
                    }
                    finally
                    {
                        seed = null;
                    }

                    if (request.Verify)
                    {
                        actions.Add("Verify via /validate/check");
                        var code = _local.GenerateCurrentCode();
                        var ok = await client.ValidateCheckAsync(request.TargetUsername, request.Profile.Realm, code, ct)
                            .ConfigureAwait(false);
                        if (!ok)
                        {
                            return new OperationResult
                            {
                                Success = false,
                                ExitCode = ExitCode.VerifyFailed,
                                Message = "Post-bind verification failed.",
                                User = request.TargetUsername,
                                OldSerial = oldSerial,
                                NewSerial = newSerial,
                                Hostname = hostname,
                                HorizonVersion = horizonVersion,
                                Actions = actions
                            };
                        }
                    }
                }
                else
                {
                    actions.Add("Bind local MFA (what-if)");
                    if (request.Verify)
                        actions.Add("Verify via /validate/check (what-if)");
                }

                return new OperationResult
                {
                    Success = true,
                    ExitCode = ExitCode.Success,
                    Message = request.WhatIf ? "What-if completed." : "Onboard completed.",
                    User = request.TargetUsername,
                    OldSerial = oldSerial,
                    NewSerial = newSerial,
                    Hostname = hostname,
                    HorizonVersion = horizonVersion,
                    WhatIf = request.WhatIf,
                    Actions = actions
                };
            }
            finally
            {
                (client as IDisposable)?.Dispose();
            }
        }
        catch (PrivacyIdeaException ex)
        {
            return new OperationResult
            {
                Success = false,
                ExitCode = ExitCode.AuthFailed,
                Message = ex.Message,
                User = request.TargetUsername,
                Hostname = hostname,
                Actions = actions
            };
        }
        catch (Exception ex)
        {
            return new OperationResult
            {
                Success = false,
                ExitCode = ExitCode.GeneralFailure,
                Message = ex.Message,
                User = request.TargetUsername,
                Hostname = hostname,
                Actions = actions
            };
        }
    }

    public async Task<OperationResult> RevokeAsync(
        ConnectionProfile profile,
        AdminSession session,
        string username,
        bool clearLocal,
        bool whatIf,
        CancellationToken ct = default)
    {
        var actions = new List<string>();
        var client = _piFactory(profile);
        try
        {
            var tokens = TokenPolicy.SelectTokensToReplace(
                await client.ListTokensAsync(session, profile.Realm, username, ct).ConfigureAwait(false));
            foreach (var token in tokens)
            {
                actions.Add($"Delete token {token.Serial}");
                if (!whatIf)
                    await client.DeleteTokenAsync(session, token.Serial, ct).ConfigureAwait(false);
            }

            if (clearLocal)
            {
                actions.Add("Clear local MFA credential");
                if (!whatIf)
                    await _local.ClearAsync(ct).ConfigureAwait(false);
            }

            return new OperationResult
            {
                Success = true,
                ExitCode = ExitCode.Success,
                Message = whatIf ? "What-if revoke completed." : "Revoke completed.",
                User = username,
                Hostname = Environment.MachineName,
                WhatIf = whatIf,
                Actions = actions
            };
        }
        finally
        {
            (client as IDisposable)?.Dispose();
        }
    }

    public static UserProvisionStatus Classify(string username, IReadOnlyList<PiTokenInfo> tokens, LocalMfaStatus local)
    {
        var managed = TokenPolicy.SelectTokensToReplace(tokens);
        if (managed.Count == 0)
            return UserProvisionStatus.Unprovisioned;

        var upnMatch = !string.IsNullOrWhiteSpace(local.BoundUpn)
            && (local.BoundUpn.Equals(username, StringComparison.OrdinalIgnoreCase)
                || local.BoundUpn.StartsWith(username + "@", StringComparison.OrdinalIgnoreCase));

        if (!local.IsProvisioned || !upnMatch)
            return UserProvisionStatus.NeedsRebind;

        return UserProvisionStatus.Provisioned;
    }

    private static string BuildUpn(string username, string realm)
    {
        if (username.Contains('@', StringComparison.Ordinal))
            return username;
        return $"{username}@{realm}";
    }

    private string ResolveHorizonMedia(OnboardRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.HorizonMediaPath))
            return request.HorizonMediaPath;

        var relative = request.Profile.HorizonMediaRelativePath
            ?? Path.Combine("packages", "horizon", "VMware-Horizon-Client-2309-8.11.0-22660907.exe");

        var bases = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.AppBaseDirectory))
            bases.Add(request.AppBaseDirectory);
        bases.Add(AppContext.BaseDirectory);
        bases.Add(Directory.GetCurrentDirectory());

        foreach (var b in bases)
        {
            var candidate = Path.GetFullPath(Path.Combine(b, relative));
            if (File.Exists(candidate))
                return candidate;
            // walk up looking for repo-style packages/
            var dir = new DirectoryInfo(b);
            for (var i = 0; i < 6 && dir is not null; i++)
            {
                candidate = Path.Combine(dir.FullName, relative);
                if (File.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }
        }

        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), relative));
    }
}

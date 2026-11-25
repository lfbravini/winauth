using WorkstationMfa.Core.Models;

namespace WorkstationMfa.PrivacyIdea;

public interface IPrivacyIdeaClient
{
    Task<AdminSession> AuthenticateAsync(string username, string password, string? realm = null, CancellationToken ct = default);
    Task<IReadOnlyList<PiUserInfo>> ListUsersAsync(AdminSession session, string realm, CancellationToken ct = default);
    Task<IReadOnlyList<PiTokenInfo>> ListTokensAsync(AdminSession session, string realm, string? username = null, CancellationToken ct = default);
    Task DeleteTokenAsync(AdminSession session, string serial, CancellationToken ct = default);
    Task<TotpEnrollment> InitTotpAsync(AdminSession session, string username, string realm, string description, int digits = 6, string hash = "sha1", int timestep = 30, CancellationToken ct = default);
    Task<bool> ValidateCheckAsync(string username, string realm, string otp, CancellationToken ct = default);
}

using WorkstationMfa.Core.Models;

namespace WorkstationMfa.Core.Security;

public static class TokenPolicy
{
    public const string ManagedDescriptionPrefix = "WorkstationMfa|";

    public static string BuildDescription(string hostname, DateTimeOffset at) =>
        $"{ManagedDescriptionPrefix}host={hostname}|at={at.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}";

    /// <summary>
    /// One-token product policy: all TOTP tokens for the user are treated as managed
    /// and will be replaced/destroyed on re-enrollment.
    /// </summary>
    public static bool IsManagedTotp(PiTokenInfo token) =>
        token.Tokentype.Equals("totp", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<PiTokenInfo> SelectTokensToReplace(IEnumerable<PiTokenInfo> tokens) =>
        tokens.Where(IsManagedTotp).ToList();
}

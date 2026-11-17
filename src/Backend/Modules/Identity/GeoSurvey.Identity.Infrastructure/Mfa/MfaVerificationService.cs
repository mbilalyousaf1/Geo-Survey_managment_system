using System.Security.Cryptography;
using System.Text;

namespace GeoSurvey.Identity.Infrastructure.Mfa;

/// <summary>Trusted workflow boundary for persistent controls. Does not verify a code or grant authentication.</summary>
public sealed class MfaVerificationService(IMfaVerificationStore store)
{
    public Task<MfaFailureResult> RecordFailureAsync(Guid accountPublicId, MfaChallengePurpose purpose, string? token, CancellationToken cancellationToken = default)
    {
        ValidatePurpose(purpose);
        return ValidInput(accountPublicId, token)
            ? store.RecordFailureAsync(accountPublicId, purpose, Hash(token!), cancellationToken)
            : Task.FromResult(MfaFailureResult.InvalidChallenge);
    }

    /// <summary>Call only after trusted TOTP verification, inside the completion transaction.</summary>
    public Task<bool> TryAcceptStepAsync(Guid accountPublicId, MfaChallengePurpose purpose, string? token, long matchedStep, CancellationToken cancellationToken = default)
    {
        ValidatePurpose(purpose);
        return ValidInput(accountPublicId, token)
            ? store.TryAcceptStepAsync(accountPublicId, purpose, Hash(token!), matchedStep, cancellationToken)
            : Task.FromResult(false);
    }

    private static bool ValidInput(Guid accountPublicId, string? token) => accountPublicId != Guid.Empty && token is { Length: 43 }
        && token.All(character => character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_');
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(token)));
    private static void ValidatePurpose(MfaChallengePurpose purpose)
    {
        if (!Enum.IsDefined(purpose))
            throw new ArgumentOutOfRangeException(nameof(purpose));
    }
}

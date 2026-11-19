using System.Data;
using System.Security.Cryptography;
using System.Text;
using GeoSurvey.ControlPlane.Infrastructure.Mfa;
using GeoSurvey.Identity.Infrastructure.Mfa;
using GeoSurvey.Identity.Infrastructure.SignIn;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GeoSurvey.ControlPlane.Infrastructure.SignIn;

/// <summary>Does not commit. The trusted caller must persist the eventual session in the same transaction.</summary>
public sealed class PlatformMfaVerifier : IPlatformMfaVerifier
{
    private readonly GlobalControlPlaneDbContext _context;
    private readonly ITotpSecretProtector _protector;
    private readonly TimeProvider _clock;
    private readonly TotpAuthenticator _authenticator;
    private readonly EfMfaChallengeStore _challenges;
    private readonly EfMfaVerificationStore _controls;

    public PlatformMfaVerifier(GlobalControlPlaneDbContext context, ITotpSecretProtector protector, TimeProvider clock)
    {
        _context = context;
        _protector = protector;
        _clock = clock;
        _authenticator = new TotpAuthenticator(clock);
        _challenges = new EfMfaChallengeStore(context); // Enforces sensitive-logging prohibition.
        _controls = new EfMfaVerificationStore(context, clock);
    }

    public async Task<PlatformMfaVerificationResult> VerifyAsync(string? verificationToken, string? code, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var transaction = _context.Database.CurrentTransaction;
        if (transaction is null || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.ReadCommitted)
            throw new InvalidOperationException("Platform MFA verification requires a caller-owned read-committed completion transaction.");
        if (!IsToken(verificationToken))
            return PlatformMfaVerificationResult.Rejected;
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(verificationToken!)));
        try
        {
            // Routing only: no client account/tenant assertion and no authorization based on this first read.
            // ReadCommitted releases its read locks before the required account-first update-lock sequence.
            var accountPublicId = await (from challenge in _context.Set<MfaChallenge>()
                                         join account in _context.Users on challenge.AccountId equals account.Id
                                         where challenge.TokenHash == tokenHash && challenge.Purpose == MfaChallengePurpose.Verification
                                         select (Guid?)account.PublicId).SingleOrDefaultAsync(cancellationToken);
            if (accountPublicId is null)
                return PlatformMfaVerificationResult.Rejected;
            await _controls.LockAsync(accountPublicId.Value, tokenHash, cancellationToken);
            var now = _clock.GetUtcNow();
            if (now.Offset != TimeSpan.Zero)
                throw new InvalidOperationException("The platform MFA clock must supply UTC timestamps.");
            var current = await _challenges.ValidChallenges(accountPublicId.Value, MfaChallengePurpose.Verification, tokenHash, now)
                // Password-entry policy respects active dates even on legacy rows with the flag disabled.
                .Where(challenge => !_context.Users.Any(user => user.Id == challenge.AccountId && user.LockoutEnd > now))
                .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (current is null)
                return PlatformMfaVerificationResult.Rejected;
            var credential = await _context.Set<ConfirmedTotpCredential>().AsNoTracking()
                .SingleOrDefaultAsync(row => row.AccountId == current.AccountId, cancellationToken);
            if (credential is null || !await _context.Set<MfaRecoveryCode>().AnyAsync(row => row.AccountId == current.AccountId, cancellationToken))
                return PlatformMfaVerificationResult.Rejected;

            var secret = _protector.Unprotect(accountPublicId.Value, credential.ProtectedSecret);
            if (secret.Length != TotpAuthenticator.SecretCharacterLength || secret.Any(character => character is not (>= 'A' and <= 'Z' or >= '2' and <= '7')))
                throw new CryptographicException("Confirmed authenticator material is invalid.");
            var step = _authenticator.Verify(secret, code);
            if (step is null)
            {
                // Caller commits this denied outcome to preserve the budget; infrastructure errors roll back.
                await _controls.RecordFailureAsync(accountPublicId.Value, MfaChallengePurpose.Verification, tokenHash, cancellationToken);
                return PlatformMfaVerificationResult.Rejected;
            }
            return await _controls.TryAcceptStepAsync(accountPublicId.Value, MfaChallengePurpose.Verification, tokenHash, step.Value, cancellationToken)
                ? PlatformMfaVerificationResult.Verified(accountPublicId.Value)
                : PlatformMfaVerificationResult.Rejected;
        }
        catch (Exception exception) when (exception is SqlException or DbUpdateException)
        {
            // Caller must roll back. Do not expose provider diagnostics/parameters through authentication errors.
            throw new InvalidOperationException("Platform MFA verification could not be completed.");
        }
    }

    private static bool IsToken(string? token) => token is { Length: 43 }
        && token.All(character => character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_');
}

using System.Data;
using System.Security.Cryptography;
using System.Text;
using GeoSurvey.Identity.Infrastructure.Mfa;
using Microsoft.EntityFrameworkCore;

namespace GeoSurvey.ControlPlane.Infrastructure.Mfa;

/// <summary>Owns the completion transaction; recovery codes leave this service only after commit.</summary>
public sealed class MfaEnrollmentConfirmationService : IMfaEnrollmentConfirmation
{
    private readonly GlobalControlPlaneDbContext _context;
    private readonly ITotpSecretProtector _protector;
    private readonly TimeProvider _clock;
    private readonly TotpAuthenticator _authenticator;
    private readonly EfMfaChallengeStore _challenges;
    private readonly EfMfaVerificationStore _verification;

    public MfaEnrollmentConfirmationService(GlobalControlPlaneDbContext context, ITotpSecretProtector protector, TimeProvider clock)
    {
        _context = context;
        _protector = protector;
        _clock = clock;
        _authenticator = new TotpAuthenticator(clock);
        _challenges = new EfMfaChallengeStore(context);
        _verification = new EfMfaVerificationStore(context, clock);
    }

    public async Task<MfaEnrollmentConfirmationResult> ConfirmAsync(Guid accountPublicId, string? enrollmentToken, string? code, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (accountPublicId == Guid.Empty || !IsToken(enrollmentToken))
            return MfaEnrollmentConfirmationResult.Rejected;
        if (_context.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Enrollment confirmation must own its transaction so recovery codes are returned only after commit.");
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(enrollmentToken!)));
        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await _verification.LockAsync(accountPublicId, tokenHash, cancellationToken);
        var now = UtcNow();
        var challenge = await _challenges.ValidChallenges(accountPublicId, MfaChallengePurpose.Enrollment, tokenHash, now)
            .Where(challenge => !_context.Users.Any(user => user.Id == challenge.AccountId && user.LockoutEnd > now))
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (challenge is null)
            return MfaEnrollmentConfirmationResult.Rejected;
        var pending = await _context.Set<PendingTotpEnrollment>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.AccountId == challenge.AccountId && row.ChallengeId == challenge.Id, cancellationToken);
        if (pending is null || await _context.Set<ConfirmedTotpCredential>().AnyAsync(row => row.AccountId == challenge.AccountId, cancellationToken)
            || await _context.Set<MfaRecoveryCode>().AnyAsync(row => row.AccountId == challenge.AccountId, cancellationToken))
            return MfaEnrollmentConfirmationResult.Rejected;

        var secret = _protector.Unprotect(accountPublicId, pending.ProtectedSecret);
        if (secret.Length != TotpAuthenticator.SecretCharacterLength || secret.Any(character => character is not (>= 'A' and <= 'Z' or >= '2' and <= '7')))
            throw new CryptographicException("Pending authenticator material is invalid.");
        var matchedStep = _authenticator.Verify(secret, code);
        if (matchedStep is null)
        {
            await _verification.RecordFailureAsync(accountPublicId, MfaChallengePurpose.Enrollment, tokenHash, cancellationToken);
            // Invalid codes are a committed security outcome, not an infrastructure rollback.
            await transaction.CommitAsync(cancellationToken);
            return MfaEnrollmentConfirmationResult.Rejected;
        }
        if (!await _verification.TryAcceptStepAsync(accountPublicId, MfaChallengePurpose.Enrollment, tokenHash, matchedStep.Value, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return MfaEnrollmentConfirmationResult.Rejected;
        }

        var confirmedAt = UtcNow();
        var recoveryCodes = MfaRecoveryCodes.GenerateBatch();
        var result = MfaEnrollmentConfirmationResult.Confirmed(recoveryCodes);
        await _context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [identity].[TotpCredentials] ([AccountId], [ProtectedSecret], [ConfirmedAtUtc]) VALUES ({challenge.AccountId}, {pending.ProtectedSecret}, {confirmedAt})", cancellationToken);
        foreach (var recoveryCode in recoveryCodes)
        {
            var digest = MfaRecoveryCodes.ComputeIssuedCodeDigest(accountPublicId, recoveryCode);
            await _context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [identity].[MfaRecoveryCodes] ([AccountId], [CodeHash], [CreatedAtUtc], [ConsumedAtUtc]) VALUES ({challenge.AccountId}, {digest}, {confirmedAt}, NULL)", cancellationToken);
        }
        var securityStamp = Guid.NewGuid().ToString("N");
        var concurrencyStamp = Guid.NewGuid().ToString("N");
        var updated = await _context.Users.Where(user => user.Id == challenge.AccountId && !user.TwoFactorEnabled
                && EF.Functions.Collate(user.SecurityStamp!, "Latin1_General_100_BIN2") == challenge.SecurityStamp)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.TwoFactorEnabled, true)
                .SetProperty(user => user.SecurityStamp, securityStamp).SetProperty(user => user.ConcurrencyStamp, concurrencyStamp), cancellationToken);
        if (updated != 1)
            throw new InvalidOperationException("The enrollment account changed during confirmation.");
        await _context.Set<MfaChallenge>().Where(row => row.AccountId == challenge.AccountId && row.ConsumedAtUtc == null && row.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RevokedAtUtc, (DateTimeOffset?)confirmedAt), cancellationToken);
        var removed = await _context.Set<PendingTotpEnrollment>().Where(row => row.AccountId == challenge.AccountId && row.ChallengeId == challenge.Id)
            .ExecuteDeleteAsync(cancellationToken);
        if (removed != 1)
            throw new InvalidOperationException("The pending enrollment changed during confirmation.");
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private DateTimeOffset UtcNow()
    {
        var now = _clock.GetUtcNow();
        if (now.Offset != TimeSpan.Zero)
            throw new InvalidOperationException("The enrollment confirmation clock must supply UTC timestamps.");
        return now;
    }

    private static bool IsToken(string? token) => token is { Length: 43 }
        && token.All(character => character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_');
}

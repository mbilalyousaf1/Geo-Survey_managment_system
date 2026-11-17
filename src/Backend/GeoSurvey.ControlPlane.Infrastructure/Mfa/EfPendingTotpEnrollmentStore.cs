using System.Data;
using GeoSurvey.Identity.Infrastructure.Mfa;
using Microsoft.EntityFrameworkCore;

namespace GeoSurvey.ControlPlane.Infrastructure.Mfa;

/// <summary>Scoped, transaction-aware pending storage. Shares lock order with challenge issuance: account, then challenge.</summary>
public sealed class EfPendingTotpEnrollmentStore : IPendingTotpEnrollmentStore
{
    private readonly GlobalControlPlaneDbContext _context;
    private readonly EfMfaChallengeStore _challenges;
    private readonly TimeProvider _clock;

    public EfPendingTotpEnrollmentStore(GlobalControlPlaneDbContext context, TimeProvider clock)
    {
        _context = context;
        _clock = clock;
        // Reuse the same eligibility query and sensitive-logging guard as challenge persistence.
        _challenges = new EfMfaChallengeStore(context);
    }

    public async Task<bool> TryStoreAsync(Guid accountPublicId, string enrollmentTokenHash, string protectedSecret,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedSecret);
        var ownsTransaction = _context.Database.CurrentTransaction is null;
        var transaction = _context.Database.CurrentTransaction
            ?? await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            // SecurityStamp is not covered by the PublicId/TokenHash indexes: reading it also locks
            // the base rows, preventing state updates from bypassing an index-only lock.
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT [SecurityStamp] FROM [identity].[Accounts] WITH (UPDLOCK, HOLDLOCK) WHERE [PublicId] = {accountPublicId}", cancellationToken);
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT [SecurityStamp] FROM [identity].[MfaChallenges] WITH (UPDLOCK, HOLDLOCK) WHERE [TokenHash] = {enrollmentTokenHash}", cancellationToken);
            // Read time after waiting for locks so contention cannot admit an already-expired challenge.
            var now = _clock.GetUtcNow();
            if (now.Offset != TimeSpan.Zero)
                throw new InvalidOperationException("The enrollment clock must supply UTC timestamps.");
            var challenge = await _challenges.ValidChallenges(accountPublicId, MfaChallengePurpose.Enrollment, enrollmentTokenHash, now)
                .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (challenge is null)
                return false;
            var current = await _context.Set<PendingTotpEnrollment>().AsNoTracking()
                .SingleOrDefaultAsync(row => row.AccountId == challenge.AccountId, cancellationToken);
            if (current?.ChallengeId == challenge.Id)
                return false;

            // Direct commands avoid flushing unrelated tracked entities or keeping stale ciphertext in tracking.
            if (current is null)
                await _context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [identity].[PendingTotpEnrollments] ([AccountId], [ChallengeId], [ProtectedSecret], [CreatedAtUtc]) VALUES ({challenge.AccountId}, {challenge.Id}, {protectedSecret}, {now})", cancellationToken);
            else
                await _context.Set<PendingTotpEnrollment>().Where(row => row.AccountId == challenge.AccountId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ChallengeId, challenge.Id)
                        .SetProperty(row => row.ProtectedSecret, protectedSecret).SetProperty(row => row.CreatedAtUtc, now), cancellationToken);
            if (ownsTransaction)
                await transaction.CommitAsync(cancellationToken);
            return true;
        }
        finally
        {
            if (ownsTransaction)
                await transaction.DisposeAsync();
        }
    }

    public Task<string?> ReadProtectedSecretAsync(Guid accountPublicId, string enrollmentTokenHash,
        DateTimeOffset now, CancellationToken cancellationToken) =>
        _context.Set<PendingTotpEnrollment>().Where(row =>
            _challenges.ValidChallenges(accountPublicId, MfaChallengePurpose.Enrollment, enrollmentTokenHash, now)
                .Any(challenge => challenge.Id == row.ChallengeId && challenge.AccountId == row.AccountId))
            .Select(row => row.ProtectedSecret).SingleOrDefaultAsync(cancellationToken);
}

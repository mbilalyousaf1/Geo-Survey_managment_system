using System.Data;
using GeoSurvey.Identity.Infrastructure.Mfa;
using Microsoft.EntityFrameworkCore;

namespace GeoSurvey.ControlPlane.Infrastructure.Mfa;

/// <summary>Scoped account-level controls. Reuses account-first locking and existing challenge eligibility.</summary>
public sealed class EfMfaVerificationStore : IMfaVerificationStore
{
    private readonly GlobalControlPlaneDbContext _context;
    private readonly TimeProvider _clock;
    private readonly EfMfaChallengeStore _challenges;

    public EfMfaVerificationStore(GlobalControlPlaneDbContext context, TimeProvider clock)
    {
        _context = context;
        _clock = clock;
        _challenges = new EfMfaChallengeStore(context);
    }

    public async Task<MfaFailureResult> RecordFailureAsync(Guid accountPublicId, MfaChallengePurpose purpose, string tokenHash, CancellationToken cancellationToken)
    {
        var ownsTransaction = _context.Database.CurrentTransaction is null;
        var transaction = _context.Database.CurrentTransaction
            ?? await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            await LockAsync(accountPublicId, tokenHash, cancellationToken);
            var now = UtcNow();
            var challenge = await _challenges.ValidChallenges(accountPublicId, purpose, tokenHash, now).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (challenge is null)
                return MfaFailureResult.InvalidChallenge;
            var state = await ReadStateAsync(challenge.AccountId, cancellationToken);
            var result = await IncrementFailureAsync(challenge.AccountId, state, now, cancellationToken);
            if (ownsTransaction)
                await transaction.CommitAsync(cancellationToken);
            return result;
        }
        finally
        {
            if (ownsTransaction)
                await transaction.DisposeAsync();
        }
    }

    public async Task<bool> TryAcceptStepAsync(Guid accountPublicId, MfaChallengePurpose purpose, string tokenHash, long matchedStep, CancellationToken cancellationToken)
    {
        if (_context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("MFA step acceptance requires the caller's completion transaction.");
        await LockAsync(accountPublicId, tokenHash, cancellationToken);
        var now = UtcNow();
        var challenge = await _challenges.ValidChallenges(accountPublicId, purpose, tokenHash, now).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (challenge is null)
            return false;
        var state = await ReadStateAsync(challenge.AccountId, cancellationToken);
        var currentStep = now.ToUnixTimeSeconds() / TotpAuthenticator.StepSeconds;
        if (matchedStep < 0 || matchedStep < currentStep - 1 || matchedStep > currentStep + 1
            || (state?.LastAcceptedStep is { } last && matchedStep <= last))
        {
            await IncrementFailureAsync(challenge.AccountId, state, now, cancellationToken);
            return false;
        }

        // Eligibility is rechecked by the conditional update. Caller must roll back on infrastructure errors.
        if (!await _challenges.TryConsumeAsync(accountPublicId, purpose, tokenHash, now, cancellationToken))
            return false;
        if (state is null)
            await _context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [identity].[MfaVerificationStates] ([AccountId], [FailedAttemptCount], [FailureWindowStartedAtUtc], [BlockedUntilUtc], [LastAcceptedStep]) VALUES ({challenge.AccountId}, 0, NULL, NULL, {matchedStep})", cancellationToken);
        else
            await _context.Set<MfaVerificationState>().Where(row => row.AccountId == challenge.AccountId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.FailedAttemptCount, 0)
                    .SetProperty(row => row.FailureWindowStartedAtUtc, (DateTimeOffset?)null)
                    .SetProperty(row => row.BlockedUntilUtc, (DateTimeOffset?)null)
                    .SetProperty(row => row.LastAcceptedStep, (long?)matchedStep), cancellationToken);
        return true;
    }

    private async Task<MfaFailureResult> IncrementFailureAsync(int accountId, MfaVerificationState? state, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var reset = state?.FailureWindowStartedAtUtc is null
            || state.FailureWindowStartedAtUtc.Value.Add(MfaAttemptPolicy.FailureWindow) <= now
            || (state.BlockedUntilUtc is { } blocked && blocked <= now);
        var count = reset ? 1 : state!.FailedAttemptCount + 1;
        var window = reset ? now : state!.FailureWindowStartedAtUtc!.Value;
        DateTimeOffset? blockedUntil = count >= MfaAttemptPolicy.FailureLimit ? now.Add(MfaAttemptPolicy.BlockDuration) : null;
        if (state is null)
            await _context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [identity].[MfaVerificationStates] ([AccountId], [FailedAttemptCount], [FailureWindowStartedAtUtc], [BlockedUntilUtc], [LastAcceptedStep]) VALUES ({accountId}, {count}, {window}, {blockedUntil}, NULL)", cancellationToken);
        else
            await _context.Set<MfaVerificationState>().Where(row => row.AccountId == accountId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.FailedAttemptCount, count)
                    .SetProperty(row => row.FailureWindowStartedAtUtc, (DateTimeOffset?)window)
                    .SetProperty(row => row.BlockedUntilUtc, blockedUntil), cancellationToken);
        if (blockedUntil is null)
            return MfaFailureResult.FailureRecorded;
        await _context.Set<MfaChallenge>().Where(row => row.AccountId == accountId && row.ConsumedAtUtc == null && row.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RevokedAtUtc, (DateTimeOffset?)now), cancellationToken);
        return MfaFailureResult.Blocked;
    }

    private Task<MfaVerificationState?> ReadStateAsync(int accountId, CancellationToken cancellationToken) =>
        _context.Set<MfaVerificationState>().AsNoTracking().SingleOrDefaultAsync(row => row.AccountId == accountId, cancellationToken);

    internal async Task LockAsync(Guid accountPublicId, string tokenHash, CancellationToken cancellationToken)
    {
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [SecurityStamp] FROM [identity].[Accounts] WITH (UPDLOCK, HOLDLOCK) WHERE [PublicId] = {accountPublicId}", cancellationToken);
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [SecurityStamp] FROM [identity].[MfaChallenges] WITH (UPDLOCK, HOLDLOCK) WHERE [TokenHash] = {tokenHash}", cancellationToken);
    }

    private DateTimeOffset UtcNow()
    {
        var now = _clock.GetUtcNow();
        if (now.Offset != TimeSpan.Zero)
            throw new InvalidOperationException("The MFA verification clock must supply UTC timestamps.");
        return now;
    }
}

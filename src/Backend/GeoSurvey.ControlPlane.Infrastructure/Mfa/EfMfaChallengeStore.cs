using System.Data;
using GeoSurvey.Identity.Infrastructure;
using GeoSurvey.Identity.Infrastructure.Mfa;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GeoSurvey.ControlPlane.Infrastructure.Mfa;

/// <summary>Scoped store. A future completion workflow can supply an existing transaction for atomic MFA-state changes.</summary>
public sealed class EfMfaChallengeStore : IMfaChallengeStore
{
    private readonly GlobalControlPlaneDbContext _context;

    public EfMfaChallengeStore(GlobalControlPlaneDbContext context)
    {
        if (context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.IsSensitiveDataLoggingEnabled == true)
            throw new InvalidOperationException("MFA challenge persistence requires sensitive data logging to be disabled.");
        _context = context;
    }

    public async Task<bool> ReplaceAsync(Guid accountPublicId, string verifiedSecurityStamp, MfaChallengePurpose purpose,
        string tokenHash, DateTimeOffset createdAtUtc, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken)
    {
        var ownsTransaction = _context.Database.CurrentTransaction is null;
        var transaction = _context.Database.CurrentTransaction
            ?? await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            // One account-row update lock serializes issuance; held to commit, including when no challenge exists yet.
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT [Id] FROM [identity].[Accounts] WITH (UPDLOCK, HOLDLOCK) WHERE [PublicId] = {accountPublicId}", cancellationToken);
            var account = await EligibleAccounts(accountPublicId, purpose, createdAtUtc)
                .Where(user => EF.Functions.Collate(user.SecurityStamp!, "Latin1_General_100_BIN2") == verifiedSecurityStamp).SingleOrDefaultAsync(cancellationToken);
            if (account is null)
                return false;
            await _context.Set<MfaChallenge>().Where(challenge => challenge.AccountId == account.Id && challenge.Purpose == purpose
                && challenge.ConsumedAtUtc == null && challenge.RevokedAtUtc == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(challenge => challenge.RevokedAtUtc, (DateTimeOffset?)createdAtUtc), cancellationToken);
            _context.Add(MfaChallenge.Create(account.Id, purpose, tokenHash, verifiedSecurityStamp, createdAtUtc, expiresAtUtc));
            await _context.SaveChangesAsync(cancellationToken);
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

    public Task<bool> IsValidAsync(Guid accountPublicId, MfaChallengePurpose purpose, string tokenHash,
        DateTimeOffset now, CancellationToken cancellationToken) =>
        ValidChallenges(accountPublicId, purpose, tokenHash, now).AnyAsync(cancellationToken);

    public async Task<bool> TryConsumeAsync(Guid accountPublicId, MfaChallengePurpose purpose, string tokenHash,
        DateTimeOffset now, CancellationToken cancellationToken) =>
        await ValidChallenges(accountPublicId, purpose, tokenHash, now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(challenge => challenge.ConsumedAtUtc, (DateTimeOffset?)now), cancellationToken) == 1;

    private IQueryable<IdentityAccount> EligibleAccounts(Guid publicId, MfaChallengePurpose purpose, DateTimeOffset now) =>
        _context.Users.Where(user => user.PublicId == publicId && user.AccountScope == AccountScope.Platform
            && user.SecurityStamp != null && user.SecurityStamp != ""
            && (!user.LockoutEnabled || user.LockoutEnd == null || user.LockoutEnd <= now)
            && !_context.Set<MfaVerificationState>().Any(state => state.AccountId == user.Id && state.BlockedUntilUtc > now)
            && (purpose == MfaChallengePurpose.Enrollment ? !user.TwoFactorEnabled : user.TwoFactorEnabled)
            && _context.UserClaims.Any(claim => claim.UserId == user.Id && claim.ClaimType == PlatformAdministratorClaims.Type
                && claim.ClaimValue == PlatformAdministratorClaims.Administrator));

    internal IQueryable<MfaChallenge> ValidChallenges(Guid accountPublicId, MfaChallengePurpose purpose, string tokenHash, DateTimeOffset now) =>
        _context.Set<MfaChallenge>().Where(challenge => challenge.Purpose == purpose && challenge.TokenHash == tokenHash
            && challenge.ConsumedAtUtc == null && challenge.RevokedAtUtc == null && challenge.ExpiresAtUtc > now
            && EligibleAccounts(accountPublicId, purpose, now).Any(user => user.Id == challenge.AccountId
                && EF.Functions.Collate(user.SecurityStamp!, "Latin1_General_100_BIN2") == challenge.SecurityStamp));
}

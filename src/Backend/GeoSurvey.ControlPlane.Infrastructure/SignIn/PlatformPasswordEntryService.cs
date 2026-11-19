using System.Data;
using System.Security.Cryptography;
using GeoSurvey.ControlPlane.Infrastructure.Mfa;
using GeoSurvey.Identity.Infrastructure;
using GeoSurvey.Identity.Infrastructure.Mfa;
using GeoSurvey.Identity.Infrastructure.SignIn;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GeoSurvey.ControlPlane.Infrastructure.SignIn;

/// <summary>Owns a fresh context/transaction per attempt. Never creates an authenticated principal or session.</summary>
public sealed class PlatformPasswordEntryService
    (DbContextOptions<GlobalControlPlaneDbContext> options, TimeProvider clock) : IPlatformPasswordEntry
{
    private readonly PasswordHasher<IdentityAccount> _hasher = CreateHasher();
    private static readonly Lazy<DummyCredential> Dummy = new(CreateDummyCredential);

    public async Task<PlatformPasswordEntryResult> BeginAsync(string? email, string? password, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string normalizedPassword;
        string normalizedEmail;
        try
        {
            normalizedPassword = PlatformSignInPassword.NormalizeForVerification(password!);
            normalizedEmail = AccountEmail.Create(email!).NormalizedValue;
        } 
        catch (ArgumentException)
        {
            DummyWork(string.Empty);
            cancellationToken.ThrowIfCancellationRequested();
            return PlatformPasswordEntryResult.Rejected;
        }

        try
        {
            // Private lifetime prevents issuer SaveChanges from flushing caller-tracked entities.
            // Also discard inherited diagnostic sinks: credentials/stamps/hashes are not log payloads.
            var privateOptions = new DbContextOptionsBuilder<GlobalControlPlaneDbContext>(options)
                .UseLoggerFactory(NullLoggerFactory.Instance).LogTo(_ => { }, LogLevel.None)
                .EnableSensitiveDataLogging(false).Options;
            await using var context = new GlobalControlPlaneDbContext(privateOptions);
            var snapshot = await Administrators(context).AsNoTracking()
                .SingleOrDefaultAsync(user => user.NormalizedEmail == normalizedEmail, cancellationToken);
            if (snapshot is null || string.IsNullOrEmpty(snapshot.PasswordHash) || string.IsNullOrWhiteSpace(snapshot.SecurityStamp))
            {
                DummyWork(normalizedPassword);
                cancellationToken.ThrowIfCancellationRequested();
                return PlatformPasswordEntryResult.Rejected;
            }

            PasswordVerificationResult verification;
            try
            {
                verification = _hasher.VerifyHashedPassword(snapshot, snapshot.PasswordHash, normalizedPassword);
            }
            catch (FormatException)
            {
                // A malformed stored base64 hash is not a client-visible parser error.
                DummyWork(normalizedPassword);
                cancellationToken.ThrowIfCancellationRequested();
                return PlatformPasswordEntryResult.Rejected;
            }
            var replacementHash = verification == PasswordVerificationResult.SuccessRehashNeeded
                ? _hasher.HashPassword(snapshot, normalizedPassword) : null;
            cancellationToken.ThrowIfCancellationRequested();

            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT [SecurityStamp] FROM [identity].[Accounts] WITH (UPDLOCK, HOLDLOCK) WHERE [PublicId] = {snapshot.PublicId}", cancellationToken);
            var current = await Administrators(context).AsNoTracking()
                .SingleOrDefaultAsync(user => user.PublicId == snapshot.PublicId, cancellationToken);
            if (current is null || !string.Equals(current.NormalizedEmail, normalizedEmail, StringComparison.Ordinal)
                || !string.Equals(current.PasswordHash, snapshot.PasswordHash, StringComparison.Ordinal)
                || !string.Equals(current.SecurityStamp, snapshot.SecurityStamp, StringComparison.Ordinal))
                return PlatformPasswordEntryResult.Rejected;
            var now = clock.GetUtcNow();
            if (now.Offset != TimeSpan.Zero)
                throw new InvalidOperationException("The platform password clock must supply UTC timestamps.");

            // Respect existing active dates even when a legacy/bootstrap row has LockoutEnabled=false.
            if (current.LockoutEnd is { } activeUntil && activeUntil > now)
            {
                if (!current.LockoutEnabled)
                {
                    var enabledStamp = Guid.NewGuid().ToString("N");
                    await context.Users.Where(user => user.Id == current.Id).ExecuteUpdateAsync(setters => setters
                        .SetProperty(user => user.LockoutEnabled, true)
                        .SetProperty(user => user.ConcurrencyStamp, enabledStamp), cancellationToken);
                    await RevokeChallengesAsync(context, current.Id, now, cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }
                return PlatformPasswordEntryResult.Rejected;
            }

            if (verification == PasswordVerificationResult.Failed)
            {
                var previous = current.LockoutEnd is { } expired && expired <= now ? 0 : current.AccessFailedCount;
                var failures = Math.Clamp(previous, 0, PlatformPasswordPolicy.FailureLimit - 1) + 1;
                DateTimeOffset? lockoutEnd = failures == PlatformPasswordPolicy.FailureLimit ? now.Add(PlatformPasswordPolicy.LockoutDuration) : null;
                var concurrencyStamp = Guid.NewGuid().ToString("N");
                await context.Users.Where(user => user.Id == current.Id).ExecuteUpdateAsync(setters => setters
                    .SetProperty(user => user.LockoutEnabled, true).SetProperty(user => user.AccessFailedCount, failures)
                    .SetProperty(user => user.LockoutEnd, lockoutEnd).SetProperty(user => user.ConcurrencyStamp, concurrencyStamp), cancellationToken);
                if (lockoutEnd is not null)
                    await RevokeChallengesAsync(context, current.Id, now, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return PlatformPasswordEntryResult.Rejected;
            }

            var stamp = replacementHash is null ? current.SecurityStamp! : Guid.NewGuid().ToString("N");
            var hash = replacementHash ?? current.PasswordHash!;
            var updatedConcurrencyStamp = Guid.NewGuid().ToString("N");
            await context.Users.Where(user => user.Id == current.Id).ExecuteUpdateAsync(setters => setters
                .SetProperty(user => user.LockoutEnabled, true).SetProperty(user => user.AccessFailedCount, 0)
                .SetProperty(user => user.LockoutEnd, (DateTimeOffset?)null).SetProperty(user => user.PasswordHash, hash)
                .SetProperty(user => user.SecurityStamp, stamp).SetProperty(user => user.ConcurrencyStamp, updatedConcurrencyStamp), cancellationToken);
            if (replacementHash is not null)
                await RevokeChallengesAsync(context, current.Id, now, cancellationToken);

            var hasCredential = await context.Set<ConfirmedTotpCredential>().AnyAsync(row => row.AccountId == current.Id, cancellationToken);
            // Consumed recovery rows still count as issued data; exhausting backups must not disable TOTP.
            var hasRecoveryData = await context.Set<MfaRecoveryCode>().AnyAsync(row => row.AccountId == current.Id, cancellationToken);
            var inconsistent = current.TwoFactorEnabled ? !hasCredential || !hasRecoveryData : hasCredential || hasRecoveryData;
            var mfaBlocked = await context.Set<MfaVerificationState>().AnyAsync(row => row.AccountId == current.Id && row.BlockedUntilUtc > now, cancellationToken);
            if (inconsistent || mfaBlocked)
            {
                // Password success only clears its own counter. Never reset/repair MFA control or credential state.
                await transaction.CommitAsync(cancellationToken);
                return PlatformPasswordEntryResult.Rejected;
            }

            var purpose = current.TwoFactorEnabled ? MfaChallengePurpose.Verification : MfaChallengePurpose.Enrollment;
            var challenge = await new MfaChallengeService(new EfMfaChallengeStore(context), clock)
                .TryIssueAsync(current.PublicId, stamp, purpose, cancellationToken);
            if (challenge is null)
                return PlatformPasswordEntryResult.Rejected;
            var result = PlatformPasswordEntryResult.Issued(purpose, challenge);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception exception) when (exception is SqlException or DbUpdateException)
        {
            throw new InvalidOperationException("Platform credential verification could not be completed.");
        }
    }

    private static IQueryable<IdentityAccount> Administrators(GlobalControlPlaneDbContext context) =>
        context.Users.Where(user => user.AccountScope == AccountScope.Platform
            && context.UserClaims.Any(claim => claim.UserId == user.Id && claim.ClaimType == PlatformAdministratorClaims.Type
                && claim.ClaimValue == PlatformAdministratorClaims.Administrator));

    private static Task<int> RevokeChallengesAsync(GlobalControlPlaneDbContext context, int accountId, DateTimeOffset now, CancellationToken cancellationToken) =>
        context.Set<MfaChallenge>().Where(row => row.AccountId == accountId && row.ConsumedAtUtc == null && row.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RevokedAtUtc, (DateTimeOffset?)now), cancellationToken);

    private void DummyWork(string password)
    {
        var dummy = Dummy.Value;
        _ = _hasher.VerifyHashedPassword(dummy.Account, dummy.Hash, password);
    }

    private static PasswordHasher<IdentityAccount> CreateHasher()
    {
        var options = new PasswordHasherOptions();
        IdentityPasswordHashingOptions.Configure(options);
        return new PasswordHasher<IdentityAccount>(Options.Create(options));
    }

    private static DummyCredential CreateDummyCredential()
    {
        var account = IdentityAccount.CreatePlatformAccount("dummy@example.invalid");
        Span<byte> bytes = stackalloc byte[32];
        try
        {
            RandomNumberGenerator.Fill(bytes);
            return new DummyCredential(account, CreateHasher().HashPassword(account, Convert.ToHexString(bytes)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    // In-memory hashing input only: never persisted, selected by lookup, or authorized to issue a challenge.
    private sealed class DummyCredential(IdentityAccount account, string hash)
    {
        public IdentityAccount Account { get; } = account;
        public string Hash { get; } = hash;
    }
}

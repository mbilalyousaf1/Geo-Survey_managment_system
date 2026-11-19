using System.Data;
using System.Security.Cryptography;
using System.Text;
using GeoSurvey.Identity.Infrastructure;
using GeoSurvey.Identity.Infrastructure.Mfa;
using GeoSurvey.Identity.Infrastructure.Sessions;
using GeoSurvey.Identity.Infrastructure.SignIn;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeoSurvey.ControlPlane.Infrastructure.SignIn;

/// <summary>Owns the transaction across actual MFA acceptance and initial session/refresh persistence.</summary>
public sealed class PlatformSessionStarter
    (DbContextOptions<GlobalControlPlaneDbContext> options, ITotpSecretProtector protector, TimeProvider clock) : IPlatformSessionStarter
{
    public async Task<PlatformSessionStartResult> StartAsync(string? verificationToken, string? code, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("Platform session creation must own its transaction.");

        try
        {
            // Never flush a caller's tracker or inherit credential-bearing diagnostic sinks.
            var privateOptions = new DbContextOptionsBuilder<GlobalControlPlaneDbContext>(options)
                .UseLoggerFactory(NullLoggerFactory.Instance).LogTo(_ => { }, LogLevel.None)
                .EnableSensitiveDataLogging(false).Options;
            await using var context = new GlobalControlPlaneDbContext(privateOptions);
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            var verified = await new PlatformMfaVerifier(context, protector, clock).VerifyAsync(verificationToken, code, cancellationToken);
            if (!verified.IsVerified)
            {
                // Normal denials must retain any wrong-code/replay count and threshold revocation.
                await transaction.CommitAsync(cancellationToken);
                return PlatformSessionStartResult.Rejected;
            }

            // The verifier holds account/challenge locks until this transaction ends. Bind again to
            // the consumed challenge stamp, never adopt a newly supplied subject or credential version.
            var now = UtcNow();
            var challengeHash = Digest(verificationToken!);
            var account = await (from user in context.Users
                                 join challenge in context.Set<MfaChallenge>() on user.Id equals challenge.AccountId
                                 where user.PublicId == verified.AccountPublicId!.Value
                                     && user.AccountScope == AccountScope.Platform && user.TwoFactorEnabled
                                     && user.SecurityStamp != null && user.SecurityStamp != ""
                                     && challenge.TokenHash == challengeHash && challenge.Purpose == MfaChallengePurpose.Verification
                                     && challenge.ConsumedAtUtc != null && challenge.RevokedAtUtc == null
                                     && challenge.SecurityStamp == EF.Functions.Collate(user.SecurityStamp, "Latin1_General_100_BIN2")
                                     && (user.LockoutEnd == null || user.LockoutEnd <= now)
                                     && context.UserClaims.Any(claim => claim.UserId == user.Id && claim.ClaimType == PlatformAdministratorClaims.Type
                                         && claim.ClaimValue == PlatformAdministratorClaims.Administrator)
                                     && !context.Set<MfaVerificationState>().Any(state => state.AccountId == user.Id && state.BlockedUntilUtc > now)
                                     && context.Set<ConfirmedTotpCredential>().Any(credential => credential.AccountId == user.Id)
                                     && context.Set<MfaRecoveryCode>().Any(backup => backup.AccountId == user.Id)
                                 select new { user.Id, user.PublicId, user.SecurityStamp }).SingleOrDefaultAsync(cancellationToken);
            if (account is null || string.IsNullOrWhiteSpace(account.SecurityStamp))
                return PlatformSessionStartResult.Rejected; // Disposal rolls back provisional MFA acceptance.

            var created = UtcNow();
            var absoluteExpiry = created.Add(PlatformSessionPolicy.AbsoluteLifetime);
            var refreshExpiry = created.Add(PlatformSessionPolicy.RefreshInactivityWindow);
            if (refreshExpiry > absoluteExpiry)
                refreshExpiry = absoluteExpiry;
            var rawToken = GenerateToken();
            var family = TokenFamily.Create(account.Id, account.SecurityStamp, created, absoluteExpiry);
            context.Add(family);
            await context.SaveChangesAsync(cancellationToken);
            context.Add(RefreshToken.Create(family.Id, Digest(rawToken), created, refreshExpiry));
            await context.SaveChangesAsync(cancellationToken);
            // Validate/build the delivery value before commit; it cannot escape before commit succeeds.
            var result = PlatformSessionStartResult.Started(new PlatformSessionTicket(account.PublicId, family.PublicId,
                rawToken, created, refreshExpiry, absoluteExpiry));
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception exception) when (exception is SqlException or DbUpdateException)
        {
            // Do not retry an uncertain commit or include provider/parameter details in the error.
            throw new InvalidOperationException("Platform session creation could not be completed.");
        }
    }

    private DateTimeOffset UtcNow()
    {
        var now = clock.GetUtcNow();
        if (now.Offset != TimeSpan.Zero)
            throw new InvalidOperationException("The platform session clock must supply UTC timestamps.");
        return now;
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        try
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static string Digest(string token)
    {
        var bytes = Encoding.ASCII.GetBytes(token);
        Span<byte> hash = stackalloc byte[32];
        try
        {
            SHA256.HashData(bytes, hash);
            return Convert.ToHexString(hash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            CryptographicOperations.ZeroMemory(hash);
        }
    }
}

using System.Data;
using System.Security.Cryptography;
using GeoSurvey.ControlPlane.Infrastructure.Mfa;
using GeoSurvey.Identity.Application.Authentication;
using GeoSurvey.Identity.Infrastructure.Authentication;
using GeoSurvey.Identity.Infrastructure.Mfa;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GeoSurvey.ControlPlane.Infrastructure.Authentication;

/// <summary>Resolves enrollment identity from stored opaque proof; never accepts an HTTP-selected account.</summary>
public sealed class PlatformEnrollmentService(DbContextOptions<GlobalControlPlaneDbContext> options,
    ITotpSecretProtector protector, TimeProvider clock) : IPlatformEnrollment
{
    public async Task<EnrollmentMaterial?> BeginAsync(string? enrollmentToken, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AuthenticationContext.RequireOwnedTransaction();
        if (!OpaqueAuthenticationToken.IsWellFormed(enrollmentToken))
            return null;
        try
        {
            await using var context = AuthenticationContext.Create(options);
            var hash = OpaqueAuthenticationToken.Digest(enrollmentToken!);
            var accountId = await ResolveAsync(context, hash, cancellationToken);
            if (accountId is null)
                return null;
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            await new EfMfaVerificationStore(context, clock).LockAsync(accountId.Value, hash, cancellationToken);
            var challenge = await new EfMfaChallengeStore(context)
                .ValidChallenges(accountId.Value, MfaChallengePurpose.Enrollment, hash, AuthenticationContext.UtcNow(clock))
                .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (challenge is null || await context.Users.AnyAsync(user => user.Id == challenge.AccountId && user.LockoutEnd > clock.GetUtcNow(), cancellationToken))
                return null;
            var pending = new PendingTotpEnrollmentService(new EfPendingTotpEnrollmentStore(context, clock), protector, clock);
            var secret = await pending.ReadSecretAsync(accountId.Value, enrollmentToken, cancellationToken);
            if (secret is null)
            {
                secret = TotpAuthenticator.GenerateSecret();
                if (!await pending.TryStoreAsync(accountId.Value, enrollmentToken, secret, cancellationToken))
                    return null;
            }
            var email = await context.Users.Where(user => user.Id == challenge.AccountId).Select(user => user.Email).SingleAsync(cancellationToken);
            var label = Uri.EscapeDataString("GeoSurvey:" + email);
            var uri = $"otpauth://totp/{label}?secret={secret}&issuer=GeoSurvey&algorithm=SHA1&digits=6&period=30";
            var material = new EnrollmentMaterial(secret, uri, challenge.ExpiresAtUtc);
            await transaction.CommitAsync(cancellationToken);
            return material;
        }
        catch (Exception exception) when (exception is SqlException or DbUpdateException or CryptographicException)
        { throw new InvalidOperationException("Authenticator enrollment could not be completed."); }
    }

    public async Task<EnrollmentConfirmation?> ConfirmAsync(string? enrollmentToken, string? code, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AuthenticationContext.RequireOwnedTransaction();
        if (!OpaqueAuthenticationToken.IsWellFormed(enrollmentToken))
            return null;
        try
        {
            await using var context = AuthenticationContext.Create(options);
            var account = await ResolveAsync(context, OpaqueAuthenticationToken.Digest(enrollmentToken!), cancellationToken);
            if (account is null)
                return null;
            var confirmed = await new MfaEnrollmentConfirmationService(context, protector, clock)
                .ConfirmAsync(account.Value, enrollmentToken, code, cancellationToken);
            return confirmed.Succeeded ? new EnrollmentConfirmation(confirmed.RecoveryCodes) : null;
        }
        catch (Exception exception) when (exception is SqlException or DbUpdateException or CryptographicException)
        { throw new InvalidOperationException("Authenticator confirmation could not be completed."); }
    }

    private static Task<Guid?> ResolveAsync(GlobalControlPlaneDbContext context, string hash, CancellationToken cancellationToken) =>
        (from challenge in context.Set<MfaChallenge>()
         join account in context.Users on challenge.AccountId equals account.Id
         where challenge.TokenHash == hash && challenge.Purpose == MfaChallengePurpose.Enrollment
         select (Guid?)account.PublicId).SingleOrDefaultAsync(cancellationToken);
}

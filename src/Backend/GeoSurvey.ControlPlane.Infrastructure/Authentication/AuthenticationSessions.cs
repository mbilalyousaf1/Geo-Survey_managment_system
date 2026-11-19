using System.Data;
using GeoSurvey.ControlPlane.Infrastructure.Mfa;
using GeoSurvey.Identity.Application.Authentication;
using GeoSurvey.Identity.Infrastructure;
using GeoSurvey.Identity.Infrastructure.Authentication;
using GeoSurvey.Identity.Infrastructure.Mfa;
using GeoSurvey.Identity.Infrastructure.Sessions;
using GeoSurvey.Identity.Infrastructure.SignIn;
using GeoSurvey.Organizations.Domain;
using GeoSurvey.Tenancy.Application;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GeoSurvey.ControlPlane.Infrastructure.Authentication;

/// <summary>Owns fresh contexts and serialized session transactions. Only digest material is persisted.</summary>
public sealed class AuthenticationSessions(DbContextOptions<GlobalControlPlaneDbContext> options,
    ITenantAdministratorAccess tenantAccess, TimeProvider clock) : IAuthenticationSessions
{
    public async Task<SessionSubject?> ValidateAsync(Guid accountPublicId, Guid sessionPublicId, AuthenticationScope scope,
        Guid? organizationPublicId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (accountPublicId == Guid.Empty || sessionPublicId == Guid.Empty || !Enum.IsDefined(scope)
            || (scope == AuthenticationScope.Organization) != (organizationPublicId is { } id && id != Guid.Empty))
            return null;
        try
        {
            await using var context = AuthenticationContext.Create(options);
            var subject = await CurrentSubjectAsync(context, accountPublicId, sessionPublicId, scope, cancellationToken);
            if (subject is null || subject.OrganizationPublicId != organizationPublicId)
                return null;
            return await HasTenantAccessAsync(subject, cancellationToken) ? subject : null;
        }
        catch (Exception exception) when (exception is SqlException or DbUpdateException)
        { throw new InvalidOperationException("Current session validation could not be completed."); }
    }

    public async Task<SessionGrant?> RotateAsync(string? refreshToken, AuthenticationScope scope,
        Guid? expectedSessionPublicId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AuthenticationContext.RequireOwnedTransaction();
        if (!OpaqueAuthenticationToken.IsWellFormed(refreshToken) || !Enum.IsDefined(scope))
            return null;
        try
        {
            await using var context = AuthenticationContext.Create(options);
            var hash = OpaqueAuthenticationToken.Digest(refreshToken!);
            var route = await RefreshRouteAsync(context, hash, scope, cancellationToken);
            if (route is null)
                return null;
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            await LockSessionAsync(context, route.Account, route.Session, hash, cancellationToken);
            var token = await context.Set<RefreshToken>().AsNoTracking().SingleOrDefaultAsync(row => row.TokenHash == hash, cancellationToken);
            var family = await context.Set<TokenFamily>().AsNoTracking().SingleOrDefaultAsync(row => row.PublicId == route.Session, cancellationToken);
            var now = AuthenticationContext.UtcNow(clock);
            if (token is null || family is null || token.TokenFamilyId != family.Id)
                return null;
            if (token.ConsumedAtUtc is not null)
            {
                // Strict reuse detection: retain consumed evidence and revoke every descendant credential.
                await RevokeFamilyAsync(context, family.Id, now, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return null;
            }
            if (expectedSessionPublicId is { } expected && expected != family.PublicId)
                throw new AuthenticationSessionChangedException();
            var subject = await CurrentSubjectAsync(context, route.Account, route.Session, scope, cancellationToken);
            if (token.ExpiresAtUtc <= now || family.ExpiresAtUtc <= now || subject is null || !await HasTenantAccessAsync(subject, cancellationToken))
            {
                await RevokeFamilyAsync(context, family.Id, now, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return null;
            }
            var consumed = await context.Set<RefreshToken>().Where(row => row.Id == token.Id && row.ConsumedAtUtc == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ConsumedAtUtc, (DateTimeOffset?)now), cancellationToken);
            if (consumed != 1)
                throw new InvalidOperationException("Refresh rotation could not be completed.");
            var raw = OpaqueAuthenticationToken.Generate();
            var expires = now.Add(PlatformSessionPolicy.RefreshInactivityWindow);
            if (expires > family.ExpiresAtUtc)
                expires = family.ExpiresAtUtc;
            context.Add(RefreshToken.Create(family.Id, OpaqueAuthenticationToken.Digest(raw), now, expires));
            await context.SaveChangesAsync(cancellationToken);
            var grant = new SessionGrant(subject, raw, expires, family.ExpiresAtUtc);
            await transaction.CommitAsync(cancellationToken);
            return grant;
        }
        catch (Exception exception) when (exception is SqlException or DbUpdateException)
        { throw new InvalidOperationException("Refresh rotation could not be completed."); }
    }

    public async Task RevokeAsync(string? refreshToken, AuthenticationScope scope, Guid? expectedSessionPublicId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AuthenticationContext.RequireOwnedTransaction();
        if (!OpaqueAuthenticationToken.IsWellFormed(refreshToken) || !Enum.IsDefined(scope))
            return;
        try
        {
            await using var context = AuthenticationContext.Create(options);
            var hash = OpaqueAuthenticationToken.Digest(refreshToken!);
            var route = await RefreshRouteAsync(context, hash, scope, cancellationToken);
            if (route is null)
                return;
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            await LockSessionAsync(context, route.Account, route.Session, hash, cancellationToken);
            var family = await context.Set<TokenFamily>().AsNoTracking().SingleOrDefaultAsync(row => row.PublicId == route.Session, cancellationToken);
            if (family is not null && expectedSessionPublicId is { } expected && expected != family.PublicId)
                throw new AuthenticationSessionChangedException();
            if (family is not null)
                await RevokeFamilyAsync(context, family.Id, AuthenticationContext.UtcNow(clock), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is SqlException or DbUpdateException)
        { throw new InvalidOperationException("Session logout could not be completed."); }
    }

    public async Task<SessionGrant?> SignInPlatformRecoveryAsync(string? verificationToken, string? recoveryCode,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AuthenticationContext.RequireOwnedTransaction();
        if (!OpaqueAuthenticationToken.IsWellFormed(verificationToken))
            return null;
        try
        {
            await using var context = AuthenticationContext.Create(options);
            var hash = OpaqueAuthenticationToken.Digest(verificationToken!);
            var accountPublicId = await (from challenge in context.Set<MfaChallenge>()
                                         join user in context.Users on challenge.AccountId equals user.Id
                                         where challenge.TokenHash == hash && challenge.Purpose == MfaChallengePurpose.Verification
                                         select (Guid?)user.PublicId).SingleOrDefaultAsync(cancellationToken);
            if (accountPublicId is null)
                return null;
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            var controls = new EfMfaVerificationStore(context, clock);
            await controls.LockAsync(accountPublicId.Value, hash, cancellationToken);
            var now = AuthenticationContext.UtcNow(clock);
            var challenges = new EfMfaChallengeStore(context);
            var challengeRow = await challenges.ValidChallenges(accountPublicId.Value, MfaChallengePurpose.Verification, hash, now)
                .Where(challenge => !context.Users.Any(user => user.Id == challenge.AccountId && user.LockoutEnd > now))
                .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (challengeRow is null || !await context.Set<ConfirmedTotpCredential>().AnyAsync(row => row.AccountId == challengeRow.AccountId, cancellationToken))
                return null;
            var canonical = OpaqueAuthenticationToken.NormalizeRecoveryCode(recoveryCode);
            var digest = canonical is null ? null : MfaRecoveryCodes.ComputeIssuedCodeDigest(accountPublicId.Value, canonical);
            var backup = digest is null ? null : await context.Set<MfaRecoveryCode>().AsNoTracking()
                .SingleOrDefaultAsync(row => row.AccountId == challengeRow.AccountId && row.CodeHash == digest && row.ConsumedAtUtc == null, cancellationToken);
            if (backup is null)
            {
                await controls.RecordFailureAsync(accountPublicId.Value, MfaChallengePurpose.Verification, hash, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return null;
            }
            if (!await challenges.TryConsumeAsync(accountPublicId.Value, MfaChallengePurpose.Verification, hash, now, cancellationToken))
                return null;
            if (await context.Set<MfaRecoveryCode>().Where(row => row.AccountId == backup.AccountId && row.CodeHash == backup.CodeHash && row.ConsumedAtUtc == null)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ConsumedAtUtc, (DateTimeOffset?)now), cancellationToken) != 1)
                throw new InvalidOperationException("Recovery sign-in could not be completed.");
            // A recovery proof clears the shared failure budget but never changes the accepted TOTP step.
            await context.Set<MfaVerificationState>().Where(row => row.AccountId == backup.AccountId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.FailedAttemptCount, 0)
                    .SetProperty(row => row.FailureWindowStartedAtUtc, (DateTimeOffset?)null)
                    .SetProperty(row => row.BlockedUntilUtc, (DateTimeOffset?)null), cancellationToken);
            var account = await context.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == backup.AccountId
                && user.AccountScope == AccountScope.Platform && user.OrganizationId == null && user.TwoFactorEnabled
                && EF.Functions.Collate(user.SecurityStamp!, "Latin1_General_100_BIN2") == challengeRow.SecurityStamp
                && (user.LockoutEnd == null || user.LockoutEnd <= now)
                && context.UserClaims.Any(claim => claim.UserId == user.Id && claim.ClaimType == PlatformAdministratorClaims.Type
                    && claim.ClaimValue == PlatformAdministratorClaims.Administrator), cancellationToken);
            if (account is null)
                return null; // Disposal rolls back provisional challenge and backup acceptance together.
            var grant = await CreateSessionAsync(context, account, AuthenticationScope.Platform, null, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return grant;
        }
        catch (Exception exception) when (exception is SqlException or DbUpdateException)
        { throw new InvalidOperationException("Recovery sign-in could not be completed."); }
    }

    public async Task<SessionGrant?> SignInOrganizationAsync(string? organizationCode, string? email, string? password,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AuthenticationContext.RequireOwnedTransaction();
        string normalizedPassword;
        string normalizedEmail;
        OrganizationCode code;
        try
        {
            normalizedPassword = PlatformSignInPassword.NormalizeForVerification(password!);
            normalizedEmail = AccountEmail.Create(email!).NormalizedValue;
            code = OrganizationCode.Create(organizationCode!);
        }
        catch (ArgumentException)
        {
            AuthenticationContext.DummyPassword(string.Empty);
            return null;
        }
        try
        {
            await using var context = AuthenticationContext.Create(options);
            var organization = await context.Organizations.AsNoTracking().SingleOrDefaultAsync(row => row.Code == code, cancellationToken);
            var snapshot = organization is null ? null : await context.Users.AsNoTracking().SingleOrDefaultAsync(user =>
                user.OrganizationId == organization.Id && user.AccountScope == AccountScope.Organization && user.NormalizedEmail == normalizedEmail, cancellationToken);
            if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.PasswordHash) || string.IsNullOrWhiteSpace(snapshot.SecurityStamp))
            {
                AuthenticationContext.DummyPassword(normalizedPassword);
                return null;
            }
            var hasher = AuthenticationContext.CreateHasher();
            PasswordVerificationResult verification;
            try { verification = hasher.VerifyHashedPassword(snapshot, snapshot.PasswordHash, normalizedPassword); }
            catch (FormatException) { AuthenticationContext.DummyPassword(normalizedPassword); return null; }
            var replacementHash = verification == PasswordVerificationResult.SuccessRehashNeeded ? hasher.HashPassword(snapshot, normalizedPassword) : null;
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT [SecurityStamp] FROM [identity].[Accounts] WITH (UPDLOCK, HOLDLOCK) WHERE [PublicId] = {snapshot.PublicId}", cancellationToken);
            var account = await context.Users.AsNoTracking().SingleOrDefaultAsync(user => user.PublicId == snapshot.PublicId, cancellationToken);
            if (account is null || account.AccountScope != AccountScope.Organization || account.OrganizationId != organization!.Id
                || account.NormalizedEmail != normalizedEmail || !string.Equals(account.PasswordHash, snapshot.PasswordHash, StringComparison.Ordinal)
                || !string.Equals(account.SecurityStamp, snapshot.SecurityStamp, StringComparison.Ordinal))
                return null;
            var now = AuthenticationContext.UtcNow(clock);
            if (account.LockoutEnd > now)
                return null;
            if (verification == PasswordVerificationResult.Failed)
            {
                var previous = account.LockoutEnd is { } expired && expired <= now ? 0 : account.AccessFailedCount;
                var failures = Math.Clamp(previous, 0, PlatformPasswordPolicy.FailureLimit - 1) + 1;
                DateTimeOffset? until = failures == PlatformPasswordPolicy.FailureLimit ? now.Add(PlatformPasswordPolicy.LockoutDuration) : null;
                var concurrency = Guid.NewGuid().ToString("N");
                await context.Users.Where(user => user.Id == account.Id).ExecuteUpdateAsync(setters => setters
                    .SetProperty(user => user.LockoutEnabled, true).SetProperty(user => user.AccessFailedCount, failures)
                    .SetProperty(user => user.LockoutEnd, until).SetProperty(user => user.ConcurrencyStamp, concurrency), cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return null;
            }
            // Phase 1 organization authorization requires the real active grant in its own database.
            if (account.TwoFactorEnabled || !await tenantAccess.CanAccessAsync(account.PublicId, organization.PublicId, cancellationToken))
                return null;
            var stamp = replacementHash is null ? account.SecurityStamp! : Guid.NewGuid().ToString("N");
            var currentHash = replacementHash ?? account.PasswordHash!;
            var concurrencyStamp = Guid.NewGuid().ToString("N");
            await context.Users.Where(user => user.Id == account.Id).ExecuteUpdateAsync(setters => setters
                .SetProperty(user => user.LockoutEnabled, true).SetProperty(user => user.AccessFailedCount, 0)
                .SetProperty(user => user.LockoutEnd, (DateTimeOffset?)null).SetProperty(user => user.PasswordHash, currentHash)
                .SetProperty(user => user.SecurityStamp, stamp).SetProperty(user => user.ConcurrencyStamp, concurrencyStamp), cancellationToken);
            account.SecurityStamp = stamp;
            var grant = await CreateSessionAsync(context, account, AuthenticationScope.Organization, organization, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return grant;
        }
        catch (Exception exception) when (exception is SqlException or DbUpdateException)
        { throw new InvalidOperationException("Organization credential verification could not be completed."); }
    }

    private async Task<SessionGrant> CreateSessionAsync(GlobalControlPlaneDbContext context, IdentityAccount account,
        AuthenticationScope scope, Organization? organization, CancellationToken cancellationToken)
    {
        var now = AuthenticationContext.UtcNow(clock);
        var family = TokenFamily.Create(account.Id, account.SecurityStamp!, now, now.Add(PlatformSessionPolicy.AbsoluteLifetime));
        context.Add(family);
        await context.SaveChangesAsync(cancellationToken);
        var raw = OpaqueAuthenticationToken.Generate();
        var expires = now.Add(PlatformSessionPolicy.RefreshInactivityWindow);
        context.Add(RefreshToken.Create(family.Id, OpaqueAuthenticationToken.Digest(raw), now, expires));
        await context.SaveChangesAsync(cancellationToken);
        var subject = new SessionSubject(account.PublicId, family.PublicId, scope, organization?.PublicId, account.Email!,
            organization?.Code.Value, organization?.DisplayName);
        return new SessionGrant(subject, raw, expires, family.ExpiresAtUtc);
    }

    private async Task<SessionSubject?> CurrentSubjectAsync(GlobalControlPlaneDbContext context, Guid accountPublicId,
        Guid sessionPublicId, AuthenticationScope scope, CancellationToken cancellationToken)
    {
        var now = AuthenticationContext.UtcNow(clock);
        var accountScope = scope == AuthenticationScope.Platform ? AccountScope.Platform : AccountScope.Organization;
        var account = await (from family in context.Set<TokenFamily>()
                             join user in context.Users on family.AccountId equals user.Id
                             where family.PublicId == sessionPublicId && family.RevokedAtUtc == null && family.ExpiresAtUtc > now
                                 && user.PublicId == accountPublicId && user.AccountScope == accountScope
                                 && user.SecurityStamp != null && user.SecurityStamp != "" && user.PasswordHash != null
                                 && family.SecurityStamp == EF.Functions.Collate(user.SecurityStamp!, "Latin1_General_100_BIN2")
                                 && (user.LockoutEnd == null || user.LockoutEnd <= now)
                                 && context.Set<RefreshToken>().Any(token => token.TokenFamilyId == family.Id && token.ConsumedAtUtc == null && token.ExpiresAtUtc > now)
                             select user).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (account is null)
            return null;
        if (scope == AuthenticationScope.Platform)
        {
            if (account.OrganizationId is not null || !account.TwoFactorEnabled
                || !await context.UserClaims.AnyAsync(claim => claim.UserId == account.Id && claim.ClaimType == PlatformAdministratorClaims.Type
                    && claim.ClaimValue == PlatformAdministratorClaims.Administrator, cancellationToken)
                || !await context.Set<ConfirmedTotpCredential>().AnyAsync(row => row.AccountId == account.Id, cancellationToken)
                || !await context.Set<MfaRecoveryCode>().AnyAsync(row => row.AccountId == account.Id, cancellationToken)
                || await context.Set<MfaVerificationState>().AnyAsync(row => row.AccountId == account.Id && row.BlockedUntilUtc > now, cancellationToken))
                return null;
            return new SessionSubject(account.PublicId, sessionPublicId, scope, null, account.Email!);
        }
        if (account.OrganizationId is null || account.TwoFactorEnabled)
            return null;
        var organization = await context.Organizations.AsNoTracking().SingleOrDefaultAsync(row => row.Id == account.OrganizationId, cancellationToken);
        return organization is null ? null : new SessionSubject(account.PublicId, sessionPublicId, scope, organization.PublicId,
            account.Email!, organization.Code.Value, organization.DisplayName);
    }

    private Task<bool> HasTenantAccessAsync(SessionSubject subject, CancellationToken cancellationToken) =>
        subject.Scope == AuthenticationScope.Platform ? Task.FromResult(true)
            : tenantAccess.CanAccessAsync(subject.AccountPublicId, subject.OrganizationPublicId!.Value, cancellationToken);

    private static Task<RefreshRoute?> RefreshRouteAsync(GlobalControlPlaneDbContext context, string hash,
        AuthenticationScope scope, CancellationToken cancellationToken)
    {
        var accountScope = scope == AuthenticationScope.Platform ? AccountScope.Platform : AccountScope.Organization;
        return (from token in context.Set<RefreshToken>()
                join family in context.Set<TokenFamily>() on token.TokenFamilyId equals family.Id
                join account in context.Users on family.AccountId equals account.Id
                where token.TokenHash == hash && account.AccountScope == accountScope
                select new RefreshRoute(account.PublicId, family.PublicId)).SingleOrDefaultAsync(cancellationToken);
    }

    private static async Task LockSessionAsync(GlobalControlPlaneDbContext context, Guid accountPublicId, Guid sessionPublicId,
        string hash, CancellationToken cancellationToken)
    {
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [SecurityStamp] FROM [identity].[Accounts] WITH (UPDLOCK, HOLDLOCK) WHERE [PublicId] = {accountPublicId}", cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [SecurityStamp] FROM [identity].[TokenFamilies] WITH (UPDLOCK, HOLDLOCK) WHERE [PublicId] = {sessionPublicId}", cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [TokenFamilyId] FROM [identity].[RefreshTokens] WITH (UPDLOCK, HOLDLOCK) WHERE [TokenHash] = {hash}", cancellationToken);
    }

    private static Task<int> RevokeFamilyAsync(GlobalControlPlaneDbContext context, long id, DateTimeOffset now, CancellationToken cancellationToken) =>
        context.Set<TokenFamily>().Where(row => row.Id == id && row.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RevokedAtUtc, (DateTimeOffset?)now), cancellationToken);

    private sealed record RefreshRoute(Guid Account, Guid Session);
}

using System.Data;
using System.Security.Cryptography;
using GeoSurvey.Identity.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeoSurvey.ControlPlane.Infrastructure.Bootstrap;

/// <summary>Operator-only creation; not registered in the API. Uses a fresh context for every attempt.</summary>
public sealed class FirstPlatformAdministratorBootstrapper(DbContextOptions<GlobalControlPlaneDbContext> options)
{
    public const string DatabaseRole = "GeoSurveyBootstrap";
    public const string LockResource = "GeoSurvey.FirstPlatformAdministratorBootstrap";
    public const int LockTimeoutMilliseconds = 30000;
    public const int LockCommandTimeoutSeconds = 45;

    public async Task<BootstrapCreationResult> CreateAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var account = IdentityAccount.CreatePlatformAccount(email);
        var normalizedPassword = LocalPasswordBlocklist.LoadPackaged().NormalizeAndValidate(password);
        var hashingOptions = new PasswordHasherOptions();
        IdentityPasswordHashingOptions.Configure(hashingOptions);
        account.PasswordHash = new PasswordHasher<IdentityAccount>(Options.Create(hashingOptions)).HashPassword(account, normalizedPassword);
        account.SecurityStamp = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // SQL errors can contain duplicate email values even with sensitive-parameter logging off.
            var operatorOptions = new DbContextOptionsBuilder<GlobalControlPlaneDbContext>(options)
                .UseLoggerFactory(NullLoggerFactory.Instance).EnableSensitiveDataLogging(false).Options;
            await using var context = new GlobalControlPlaneDbContext(operatorOptions);
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            var acquired = await AcquireLockAsync(context, transaction, cancellationToken);
            if (acquired < 0)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                cancellationToken.ThrowIfCancellationRequested();
                return BootstrapCreationResult.LockUnavailable;
            }

            if (await context.BootstrapCompletions.AnyAsync(cancellationToken))
                return BootstrapCreationResult.AlreadyCompleted;
            if (await (from user in context.Users
                       join claim in context.UserClaims on user.Id equals claim.UserId
                       where user.AccountScope == AccountScope.Platform
                           && claim.ClaimType == PlatformAdministratorClaims.Type
                           && claim.ClaimValue == PlatformAdministratorClaims.Administrator
                       select user.Id).AnyAsync(cancellationToken))
                return BootstrapCreationResult.AdministratorAlreadyExists;
            if (await context.Users.AnyAsync(user => user.AccountScope == AccountScope.Platform
                && user.NormalizedEmail == account.NormalizedEmail, cancellationToken))
                return BootstrapCreationResult.AccountAlreadyExists;

            context.Add(account);
            await context.SaveChangesAsync(cancellationToken);
            context.UserClaims.Add(new IdentityUserClaim<int>
            {
                UserId = account.Id,
                ClaimType = PlatformAdministratorClaims.Type,
                ClaimValue = PlatformAdministratorClaims.Administrator
            });
            context.BootstrapCompletions.Add(BootstrapCompletion.Create());
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return BootstrapCreationResult.Created;
        }
        catch (Exception exception) when (exception is SqlException or DbUpdateException)
        {
            // Awaited transaction disposal rolls back before this error escapes. Do not attach SQL payloads.
            throw new InvalidOperationException("Bootstrap could not complete. Check operator permissions and database availability; no credential reset is performed.");
        }
    }

    private static async Task<int> AcquireLockAsync(GlobalControlPlaneDbContext context, IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandTimeout = LockCommandTimeoutSeconds;
        command.CommandText = """
            IF COALESCE(IS_ROLEMEMBER(@principal), 0) <> 1
                THROW 51020, 'Bootstrap operator role membership is required.', 1;
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = @resource, @LockMode = N'Exclusive', @LockOwner = N'Transaction',
                @LockTimeout = @timeout, @DbPrincipal = @principal;
            SELECT @result;
            """;
        command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = LockResource });
        command.Parameters.Add(new SqlParameter("@timeout", SqlDbType.Int) { Value = LockTimeoutMilliseconds });
        command.Parameters.Add(new SqlParameter("@principal", SqlDbType.NVarChar, 128) { Value = DatabaseRole });
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is int value ? value : throw new InvalidOperationException("Bootstrap lock acquisition returned no result.");
    }
}

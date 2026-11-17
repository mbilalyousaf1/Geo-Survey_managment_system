using System.Data;
using System.Diagnostics;
using GeoSurvey.ControlPlane.Infrastructure.Bootstrap;
using GeoSurvey.Identity.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace GeoSurvey.ControlPlane.Infrastructure.IntegrationTests;

public sealed class BootstrapServiceTests
{
    private const string Password = "synthetic bootstrap integration passphrase";
    private static FirstPlatformAdministratorBootstrapper Service(BootstrapServiceDatabase database, bool authorized = true) => new(database.OperatorOptions(authorized));

    [Fact]
    public async Task RestrictedOperator_CreatesCompleteAccountClaimAndMarker()
    {
        await using var database = await BootstrapServiceDatabase.CreateAsync();
        Assert.Equal(BootstrapCreationResult.Created, await Service(database).CreateAsync(" Alice@Example.test ", Password));
        await using var reader = database.CreateContext();
        var account = await reader.Users.SingleAsync();
        Assert.Equal(AccountScope.Platform, account.AccountScope);
        Assert.Null(account.OrganizationId);
        Assert.Equal("ALICE@EXAMPLE.TEST", account.NormalizedEmail);
        Assert.False(account.TwoFactorEnabled);
        Assert.False(account.EmailConfirmed);
        Assert.False(string.IsNullOrWhiteSpace(account.SecurityStamp));
        var options = new PasswordHasherOptions();
        IdentityPasswordHashingOptions.Configure(options);
        Assert.Equal(PasswordVerificationResult.Success, new PasswordHasher<IdentityAccount>(Options.Create(options))
            .VerifyHashedPassword(account, account.PasswordHash!, BootstrapPassword.NormalizeAndValidateBase(Password)));
        var claim = await reader.UserClaims.SingleAsync();
        Assert.Equal(account.Id, claim.UserId);
        Assert.Equal(PlatformAdministratorClaims.Type, claim.ClaimType);
        Assert.Equal(PlatformAdministratorClaims.Administrator, claim.ClaimValue);
        Assert.Equal(TimeSpan.Zero, (await reader.BootstrapCompletions.SingleAsync()).CompletedAtUtc.Offset);
    }

    [Fact]
    public async Task ConcurrentAttempts_CreateExactlyOneAdministrator()
    {
        await using var database = await BootstrapServiceDatabase.CreateAsync();
        var service = Service(database);
        var results = await Task.WhenAll(service.CreateAsync("first@example.test", Password), service.CreateAsync("second@example.test", Password));
        Assert.Single(results, result => result == BootstrapCreationResult.Created);
        Assert.Single(results, result => result == BootstrapCreationResult.AlreadyCompleted);
        await using var reader = database.CreateContext();
        Assert.Equal(1, await reader.Users.CountAsync());
        Assert.Equal(1, await reader.UserClaims.CountAsync());
        Assert.Equal(1, await reader.BootstrapCompletions.CountAsync());
    }

    [Fact]
    public async Task ClaimWriteFailure_RollsBackAccountAndAllowsCleanRetry()
    {
        await using var database = await BootstrapServiceDatabase.CreateAsync();
        await using var administrator = database.CreateContext();
        await administrator.Database.ExecuteSqlRawAsync("""
            ALTER TABLE [identity].[AccountClaims] ADD CONSTRAINT [CK_TestRejectBootstrapClaim]
            CHECK ([ClaimType] <> N'geosurvey:platform-role');
            """);
        var loggerProvider = new RecordingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(loggerProvider).SetMinimumLevel(LogLevel.Trace));
        var callerOptions = new DbContextOptionsBuilder<GlobalControlPlaneDbContext>(database.OperatorOptions())
            .UseLoggerFactory(loggerFactory).EnableSensitiveDataLogging().Options;
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new FirstPlatformAdministratorBootstrapper(callerOptions).CreateAsync("failure@example.test", Password));
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain("failure@example.test", exception.ToString());
        Assert.DoesNotContain(Password, exception.ToString());
        Assert.Empty(loggerProvider.Messages);
        Assert.Equal(0, await administrator.Users.CountAsync());
        Assert.Equal(0, await administrator.UserClaims.CountAsync());
        Assert.Equal(0, await administrator.BootstrapCompletions.CountAsync());
        await administrator.Database.ExecuteSqlRawAsync("ALTER TABLE [identity].[AccountClaims] DROP CONSTRAINT [CK_TestRejectBootstrapClaim];");
        Assert.Equal(BootstrapCreationResult.Created, await Service(database).CreateAsync("retry@example.test", Password));
    }

    [Fact]
    public async Task Rerun_PreservesCredentialsAndCompletionSurvivesDeletion()
    {
        await using var database = await BootstrapServiceDatabase.CreateAsync();
        var service = Service(database);
        Assert.Equal(BootstrapCreationResult.Created, await service.CreateAsync("original@example.test", Password));
        await using var administrator = database.CreateContext();
        var original = await administrator.Users.AsNoTracking().SingleAsync();
        var timestamp = (await administrator.BootstrapCompletions.SingleAsync()).CompletedAtUtc;
        Assert.Equal(BootstrapCreationResult.AlreadyCompleted, await service.CreateAsync("original@example.test", "different synthetic bootstrap passphrase"));
        var unchanged = await administrator.Users.AsNoTracking().SingleAsync();
        Assert.Equal(original.PasswordHash, unchanged.PasswordHash);
        Assert.Equal(original.SecurityStamp, unchanged.SecurityStamp);
        Assert.Equal(original.PublicId, unchanged.PublicId);
        await administrator.Database.ExecuteSqlRawAsync("DELETE FROM [identity].[AccountClaims]; DELETE FROM [identity].[Accounts];");
        Assert.Equal(BootstrapCreationResult.AlreadyCompleted, await service.CreateAsync("replacement@example.test", Password));
        Assert.Empty(await administrator.Users.ToListAsync());
        Assert.Equal(timestamp, (await administrator.BootstrapCompletions.SingleAsync()).CompletedAtUtc);
    }

    [Fact]
    public async Task ExistingLockedAdministratorWithoutMarker_IsNotAdoptedOrReplaced()
    {
        await using var database = await BootstrapServiceDatabase.CreateAsync();
        await using var administrator = database.CreateContext();
        var existing = IdentityAccount.CreatePlatformAccount("existing@example.test");
        existing.LockoutEnabled = true;
        existing.LockoutEnd = DateTimeOffset.UtcNow.AddDays(1);
        administrator.Add(existing);
        await administrator.SaveChangesAsync();
        administrator.UserClaims.Add(new IdentityUserClaim<int>
        {
            UserId = existing.Id,
            ClaimType = PlatformAdministratorClaims.Type,
            ClaimValue = PlatformAdministratorClaims.Administrator
        });
        await administrator.SaveChangesAsync();
        Assert.Equal(BootstrapCreationResult.AdministratorAlreadyExists, await Service(database).CreateAsync("new@example.test", Password));
        Assert.Equal(1, await administrator.Users.CountAsync());
        Assert.Equal(0, await administrator.BootstrapCompletions.CountAsync());
    }

    [Fact]
    public async Task ExistingNonAdministratorEmail_IsNeverPromotedOrReset()
    {
        await using var database = await BootstrapServiceDatabase.CreateAsync();
        await using var administrator = database.CreateContext();
        administrator.Add(IdentityAccount.CreatePlatformAccount("existing@example.test"));
        await administrator.SaveChangesAsync();
        Assert.Equal(BootstrapCreationResult.AccountAlreadyExists, await Service(database).CreateAsync("EXISTING@example.test", Password));
        Assert.Equal(1, await administrator.Users.CountAsync());
        Assert.Equal(0, await administrator.UserClaims.CountAsync());
        Assert.Equal(0, await administrator.BootstrapCompletions.CountAsync());
    }

    [Fact]
    public async Task NonMember_CannotUseBootstrapAndOperatorCannotEraseMarkerOrAlterSchema()
    {
        await using var database = await BootstrapServiceDatabase.CreateAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(database, false).CreateAsync("denied@example.test", Password));
        Assert.Null(error.InnerException);
        await using (var reader = database.CreateContext())
            Assert.Equal(0, await reader.Users.CountAsync());
        Assert.Equal(BootstrapCreationResult.Created, await Service(database).CreateAsync("allowed@example.test", Password));
        await using var restricted = new GlobalControlPlaneDbContext(database.OperatorOptions());
        foreach (var sql in new[]
        {
            "DELETE FROM [controlplane].[BootstrapCompletion];",
            "UPDATE [controlplane].[BootstrapCompletion] SET [CompletedAtUtc] = SYSDATETIMEOFFSET();",
            "UPDATE [identity].[Accounts] SET [AccessFailedCount] = 1;",
            "CREATE TABLE [controlplane].[UnauthorizedTable] ([Id] int);"
        })
            await Assert.ThrowsAsync<SqlException>(() => restricted.Database.ExecuteSqlRawAsync(sql));
    }

    [Fact]
    public async Task LockTimeout_WritesNothingAndSubsequentAttemptCanSucceed()
    {
        await using var database = await BootstrapServiceDatabase.CreateAsync();
        await using var holder = database.CreateContext();
        await using var transaction = await holder.Database.BeginTransactionAsync();
        await using var command = holder.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "EXEC sys.sp_getapplock @Resource = @resource, @LockMode = N'Exclusive', @LockOwner = N'Transaction', @DbPrincipal = N'GeoSurveyBootstrap';";
        command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = FirstPlatformAdministratorBootstrapper.LockResource });
        await command.ExecuteNonQueryAsync();
        var timer = Stopwatch.StartNew();
        Assert.Equal(BootstrapCreationResult.LockUnavailable, await Service(database).CreateAsync("timeout@example.test", Password));
        Assert.True(timer.Elapsed >= TimeSpan.FromSeconds(29));
        await transaction.RollbackAsync();
        await using var reader = database.CreateContext();
        Assert.Equal(0, await reader.Users.CountAsync());
        Assert.Equal(0, await reader.UserClaims.CountAsync());
        Assert.Equal(0, await reader.BootstrapCompletions.CountAsync());
        Assert.Equal(BootstrapCreationResult.Created, await Service(database).CreateAsync("retry@example.test", Password));
    }

    [Fact]
    public async Task InvalidPasswordAndCancellation_DoNotWriteAccounts()
    {
        await using var database = await BootstrapServiceDatabase.CreateAsync();
        var service = Service(database);
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync("invalid@example.test", "passwordpassword"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CreateAsync("canceled@example.test", Password, new CancellationToken(true)));
        await using var reader = database.CreateContext();
        Assert.Equal(0, await reader.Users.CountAsync());
        Assert.Equal(0, await reader.UserClaims.CountAsync());
        Assert.Equal(0, await reader.BootstrapCompletions.CountAsync());
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = [];
        public ILogger CreateLogger(string categoryName) => new RecordingLogger(Messages);
        public void Dispose() { }

        private sealed class RecordingLogger(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                messages.Add(formatter(state, exception));
        }
    }
}

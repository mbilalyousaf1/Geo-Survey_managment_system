using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GeoSurvey.Identity.Infrastructure.IntegrationTests;

public sealed class AccountMappingTests(IdentityDatabase database) : IClassFixture<IdentityDatabase>
{
    private static string NewEmail() => "user-" + Guid.NewGuid().ToString("N") + "@example.test";

    private static IdentityAccount NewAccount(int? organizationId = null, string? email = null) =>
        organizationId is int id
            ? IdentityAccount.CreateOrganizationAccount(id, email ?? NewEmail())
            : IdentityAccount.CreatePlatformAccount(email ?? NewEmail());

    private static async Task AssertSqlRejectionAsync(Func<Task> action, params int[] numbers)
    {
        var exception = await Assert.ThrowsAsync<DbUpdateException>(action);
        var sql = Assert.IsType<SqlException>(exception.InnerException);
        Assert.Contains(sql.Number, numbers);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(101)]
    public async Task FreshRead_PreservesAccountFields(int? organizationId)
    {
        var original = NewAccount(organizationId, " MiXeD+" + Guid.NewGuid().ToString("N") + "@example.test ");
        await using (var writer = database.CreateContext())
        {
            writer.Add(original);
            await writer.SaveChangesAsync();
        }
        await using var reader = database.CreateContext();
        var loaded = await reader.Users.SingleAsync(account => account.PublicId == original.PublicId);
        Assert.NotSame(original, loaded);
        Assert.True(loaded.Id > 0);
        Assert.Equal(original.Id, loaded.Id);
        Assert.Equal(original.PublicId, loaded.PublicId);
        Assert.Equal(original.AccountScope, loaded.AccountScope);
        Assert.Equal(original.OrganizationId, loaded.OrganizationId);
        Assert.Equal(original.Email, loaded.Email);
        Assert.Equal(original.NormalizedEmail, loaded.NormalizedEmail);
        Assert.Equal(original.UserName, loaded.UserName);
        Assert.Equal(original.NormalizedUserName, loaded.NormalizedUserName);
        Assert.Equal(original.SecurityStamp, loaded.SecurityStamp);
        Assert.Equal(original.ConcurrencyStamp, loaded.ConcurrencyStamp);
    }

    [Fact]
    public async Task SameEmailAcrossOrganizationsAndPlatform_IsAllowedAndLookupsStayScoped()
    {
        var email = NewEmail();
        var first = NewAccount(201, email);
        var second = NewAccount(202, email.ToUpperInvariant());
        var platform = NewAccount(email: email);
        await using var context = database.CreateContext();
        context.AddRange(first, second, platform);
        Assert.Equal(3, await context.SaveChangesAsync());
        context.ChangeTracker.Clear();

        var loaded = await context.Users.SingleAsync(account => account.AccountScope == AccountScope.Organization
            && account.OrganizationId == 201 && account.NormalizedEmail == first.NormalizedEmail);
        Assert.Equal(first.PublicId, loaded.PublicId);
        Assert.Equal(3, await context.Users.CountAsync(account => account.NormalizedEmail == first.NormalizedEmail));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(301)]
    public async Task ConcurrentNormalizedEmailDuplicates_ExactlyOneSucceeds(int? organizationId)
    {
        var email = NewEmail();
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        first.Add(NewAccount(organizationId, email));
        second.Add(NewAccount(organizationId, email.ToUpperInvariant()));

        static async Task<bool> SaveAsync(IdentityTestContext context)
        {
            try
            {
                await context.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
            {
                return false;
            }
        }

        var results = await Task.WhenAll(SaveAsync(first), SaveAsync(second));
        Assert.Single(results, result => result);
        Assert.Single(results, result => !result);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(99, 1)]
    [InlineData(1, 1)]
    [InlineData(2, null)]
    [InlineData(2, 0)]
    [InlineData(2, -1)]
    public async Task InvalidScopeOrganizationCombination_IsRejected(int scope, int? organizationId)
    {
        await using var context = database.CreateContext();
        var account = NewAccount();
        context.Add(account);
        context.Entry(account).Property(item => item.AccountScope).CurrentValue = (AccountScope)scope;
        context.Entry(account).Property(item => item.OrganizationId).CurrentValue = organizationId;
        await AssertSqlRejectionAsync(() => context.SaveChangesAsync(), 547);
    }

    [Theory]
    [InlineData(nameof(IdentityAccount.PublicId))]
    [InlineData(nameof(IdentityAccount.NormalizedUserName))]
    public async Task DuplicateGlobalIdentifier_IsRejected(string property)
    {
        await using var context = database.CreateContext();
        var original = NewAccount();
        context.Add(original);
        await context.SaveChangesAsync();
        var duplicate = NewAccount();
        context.Add(duplicate);
        context.Entry(duplicate).Property(property).CurrentValue = context.Entry(original).Property(property).CurrentValue;
        await AssertSqlRejectionAsync(() => context.SaveChangesAsync(), 2601, 2627);
    }

    [Theory]
    [InlineData(nameof(IdentityAccount.Email), 254)]
    [InlineData(nameof(IdentityAccount.NormalizedEmail), 254)]
    [InlineData(nameof(IdentityAccount.UserName), 32)]
    [InlineData(nameof(IdentityAccount.NormalizedUserName), 32)]
    public async Task StringColumns_AcceptLimitRejectOverflowAndNull(string property, int limit)
    {
        // Deliberately bypass factories to verify storage shape, not email syntax validation.
        await using (var valid = database.CreateContext())
        {
            var account = NewAccount();
            valid.Add(account);
            valid.Entry(account).Property(property).CurrentValue = new string('x', limit);
            Assert.Equal(1, await valid.SaveChangesAsync());
        }
        await using (var oversized = database.CreateContext())
        {
            var account = NewAccount();
            oversized.Add(account);
            oversized.Entry(account).Property(property).CurrentValue = new string('x', limit + 1);
            await AssertSqlRejectionAsync(() => oversized.SaveChangesAsync(), 2628, 8152);
        }
        await using var missing = database.CreateContext();
        var invalid = NewAccount();
        missing.Add(invalid);
        missing.Entry(invalid).Property(property).CurrentValue = null;
        await AssertSqlRejectionAsync(() => missing.SaveChangesAsync(), 515);
    }

    [Fact]
    public async Task EmailPunctuation_IsDistinctAndRawPaddingBehaviorIsExplicit()
    {
        var prefix = "person" + Guid.NewGuid().ToString("N");
        var first = NewAccount(email: prefix + ".field@example.test");
        var second = NewAccount(email: prefix + "field@example.test");
        var third = NewAccount(email: prefix + ".field+tag@example.test");
        await using var context = database.CreateContext();
        context.AddRange(first, second, third);
        Assert.Equal(3, await context.SaveChangesAsync());

        // SQL Server pads comparisons even with BIN2; canonical writes remain an application duty.
        var padded = NewAccount();
        padded.NormalizedEmail = first.NormalizedEmail + " ";
        context.Add(padded);
        await AssertSqlRejectionAsync(() => context.SaveChangesAsync(), 2601, 2627);
    }

    [Fact]
    public async Task RawNonCanonicalCasing_IsNotNormalizedByDatabase()
    {
        var first = NewAccount();
        var second = NewAccount();
        second.NormalizedEmail = first.NormalizedEmail!.ToLowerInvariant();
        await using var context = database.CreateContext();
        context.AddRange(first, second);
        Assert.Equal(2, await context.SaveChangesAsync());
        // This is deliberately invalid application input: BIN2 does not uppercase it for us.
        Assert.NotEqual(first.NormalizedEmail, second.NormalizedEmail);
    }

    [Fact]
    public async Task IdentityStoreUpdate_RejectsStaleConcurrencyStamp()
    {
        var original = NewAccount();
        await using (var seed = database.CreateContext())
        {
            seed.Add(original);
            await seed.SaveChangesAsync();
        }
        await using var first = database.CreateContext();
        await using var stale = database.CreateContext();
        var firstAccount = await first.Users.SingleAsync(account => account.Id == original.Id);
        var staleAccount = await stale.Users.SingleAsync(account => account.Id == original.Id);
        using var firstStore = new UserOnlyStore<IdentityAccount, IdentityTestContext, int>(first);
        using var staleStore = new UserOnlyStore<IdentityAccount, IdentityTestContext, int>(stale);
        firstAccount.AccessFailedCount = 1;
        Assert.True((await firstStore.UpdateAsync(firstAccount)).Succeeded);
        staleAccount.AccessFailedCount = 2;
        var result = await staleStore.UpdateAsync(staleAccount);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure));
        await using var reader = database.CreateContext();
        Assert.Equal(1, (await reader.Users.SingleAsync(account => account.Id == original.Id)).AccessFailedCount);
    }

    [Fact]
    public void Model_ContainsOnlyReviewedTablesAndAccountConstraints()
    {
        using var context = database.CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        Assert.Equal(new[] { "AccountClaims", "AccountLogins", "AccountTokens", "Accounts" },
            model.GetEntityTypes().Select(entity => entity.GetTableName()!).Order(StringComparer.Ordinal).ToArray());
        Assert.All(model.GetEntityTypes(), entity => Assert.Equal("identity", entity.GetSchema()));
        var account = model.FindEntityType(typeof(IdentityAccount))!;
        Assert.Empty(account.GetForeignKeys()); // Explicitly deferred Organization FK, not a complete global model.
        Assert.Equal(ValueGenerated.OnAdd, account.FindProperty(nameof(IdentityAccount.Id))!.ValueGenerated);
        Assert.Equal(ValueGenerated.Never, account.FindProperty(nameof(IdentityAccount.PublicId))!.ValueGenerated);
        Assert.Null(account.FindProperty(nameof(IdentityAccount.PublicId))!.GetDefaultValueSql());
        Assert.True(account.FindProperty(nameof(IdentityAccount.ConcurrencyStamp))!.IsConcurrencyToken);
        Assert.Single(account.GetCheckConstraints());
        Assert.Equal(4, account.GetIndexes().Count());
        foreach (var name in new[] { nameof(IdentityAccount.NormalizedEmail), nameof(IdentityAccount.NormalizedUserName) })
            Assert.Equal("Latin1_General_100_BIN2", account.FindProperty(name)!.GetCollation());
        Assert.Equal("[AccountScope] = 1", account.GetIndexes().Single(index => index.GetDatabaseName() == "UX_Accounts_PlatformEmail").GetFilter());
        Assert.Equal("[AccountScope] = 2", account.GetIndexes().Single(index => index.GetDatabaseName() == "UX_Accounts_OrganizationEmail").GetFilter());
    }
}

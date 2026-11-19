using System.Security.Cryptography;
using GeoSurvey.Identity.Infrastructure;
using GeoSurvey.Identity.Infrastructure.Sessions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GeoSurvey.ControlPlane.Infrastructure.IntegrationTests;

public sealed class SessionStorageTests(MigrationDatabase database) : IClassFixture<MigrationDatabase>
{
    // Explicit test intervals are fixtures, not selected application session lifetimes.
    private static readonly DateTimeOffset Created = new(2026, 11, 18, 0, 0, 0, TimeSpan.Zero);
    private static string NewDigest() => Convert.ToHexString(SHA256.HashData(Guid.NewGuid().ToByteArray()));

    private async Task<TokenFamily> FamilyAsync()
    {
        await using var context = database.CreateContext();
        var account = IdentityAccount.CreatePlatformAccount(Guid.NewGuid().ToString("N") + "@example.test");
        account.SecurityStamp = Guid.NewGuid().ToString("N");
        context.Add(account);
        await context.SaveChangesAsync();
        var family = TokenFamily.Create(account.Id, account.SecurityStamp, Created, Created.AddHours(2));
        context.Add(family);
        await context.SaveChangesAsync();
        return family;
    }

    private static async Task AssertConstraintAsync(Func<Task> action, string constraint)
    {
        var error = await Assert.ThrowsAsync<DbUpdateException>(action);
        var sql = Assert.IsType<SqlException>(error.InnerException);
        Assert.Equal(547, sql.Number);
        Assert.Contains(constraint, sql.Message, StringComparison.Ordinal);
    }

    private static async Task AssertUniqueAsync(Func<Task> action)
    {
        var error = await Assert.ThrowsAsync<DbUpdateException>(action);
        Assert.Contains(Assert.IsType<SqlException>(error.InnerException).Number, new[] { 2601, 2627 });
    }

    [Fact]
    public async Task RoundTrip_PreservesBindingDigestAndUtcPrecision_WithoutPlaintextColumns()
    {
        var family = await FamilyAsync();
        var now = Created.AddTicks(1234567);
        var token = RefreshToken.Create(family.Id, NewDigest(), now, now.AddMinutes(10));
        await using (var writer = database.CreateContext())
        {
            writer.Add(token);
            await writer.SaveChangesAsync();
        }
        await using var reader = database.CreateContext();
        var loaded = await reader.Set<RefreshToken>().SingleAsync(row => row.Id == token.Id);
        Assert.Equal(family.Id, loaded.TokenFamilyId);
        Assert.Equal(token.TokenHash, loaded.TokenHash);
        Assert.Equal(now, loaded.CreatedAtUtc);
        Assert.Equal(token.ExpiresAtUtc, loaded.ExpiresAtUtc);
        Assert.Null(loaded.ConsumedAtUtc);
        var session = await reader.Set<TokenFamily>().SingleAsync(row => row.Id == family.Id);
        Assert.Equal(family.PublicId, session.PublicId);
        Assert.Equal(family.AccountId, session.AccountId);
        Assert.Equal(family.SecurityStamp, session.SecurityStamp);
        Assert.Equal(family.CreatedAtUtc, session.CreatedAtUtc);
        Assert.Equal(family.ExpiresAtUtc, session.ExpiresAtUtc);
        Assert.Null(session.RevokedAtUtc);

        var columns = await reader.Database.SqlQuery<string>(
            $"SELECT [t].[name] + '.' + [c].[name] AS [Value] FROM [sys].[columns] [c] INNER JOIN [sys].[tables] [t] ON [c].[object_id] = [t].[object_id] INNER JOIN [sys].[schemas] [s] ON [t].[schema_id] = [s].[schema_id] WHERE [s].[name] = N'identity' AND [t].[name] IN (N'TokenFamilies', N'RefreshTokens')").ToListAsync();
        Assert.Equal(new[] { "RefreshTokens.ConsumedAtUtc", "RefreshTokens.CreatedAtUtc", "RefreshTokens.ExpiresAtUtc", "RefreshTokens.Id", "RefreshTokens.TokenFamilyId", "RefreshTokens.TokenHash", "TokenFamilies.AccountId", "TokenFamilies.CreatedAtUtc", "TokenFamilies.ExpiresAtUtc", "TokenFamilies.Id", "TokenFamilies.PublicId", "TokenFamilies.RevokedAtUtc", "TokenFamilies.SecurityStamp" }, columns.Order(StringComparer.Ordinal));
        Assert.Equal(typeof(long), reader.Model.FindEntityType(typeof(TokenFamily))!.FindPrimaryKey()!.Properties.Single().ClrType);
        Assert.Equal(typeof(long), reader.Model.FindEntityType(typeof(RefreshToken))!.FindPrimaryKey()!.Properties.Single().ClrType);
    }

    [Fact]
    public async Task FamilyPublicId_MustBeGloballyUnique()
    {
        var first = await FamilyAsync();
        var second = await FamilyAsync();
        await using var context = database.CreateContext();
        context.Attach(second);
        context.Entry(second).Property(row => row.PublicId).CurrentValue = first.PublicId;
        await AssertUniqueAsync(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task EmptyFamilyPublicId_IsRejectedBySql()
    {
        var family = await FamilyAsync();
        await using var context = database.CreateContext();
        context.Attach(family);
        context.Entry(family).Property(row => row.PublicId).CurrentValue = Guid.Empty;
        await AssertConstraintAsync(() => context.SaveChangesAsync(), "CK_TokenFamilies_PublicId");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyFamilyStamp_IsRejectedBySql(string stamp)
    {
        var family = await FamilyAsync();
        await using var context = database.CreateContext();
        context.Attach(family);
        context.Entry(family).Property(row => row.SecurityStamp).CurrentValue = stamp;
        await AssertConstraintAsync(() => context.SaveChangesAsync(), "CK_TokenFamilies_SecurityStamp");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RequiredStoredValues_CannotBeNull(bool familyValue)
    {
        var family = await FamilyAsync();
        await using var context = database.CreateContext();
        if (familyValue)
        {
            context.Attach(family);
            context.Entry(family).Property(row => row.SecurityStamp).CurrentValue = null!;
        }
        else
        {
            var token = RefreshToken.Create(family.Id, NewDigest(), Created, Created.AddMinutes(5));
            context.Add(token);
            context.Entry(token).Property(row => row.TokenHash).CurrentValue = null!;
        }
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(515, Assert.IsType<SqlException>(error.InnerException).Number);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ForeignKeys_RejectMissingAccountOrFamily(bool familyRow)
    {
        await using var context = database.CreateContext();
        if (familyRow)
            context.Add(TokenFamily.Create(int.MaxValue, "stamp", Created, Created.AddHours(1)));
        else
            context.Add(RefreshToken.Create(long.MaxValue, NewDigest(), Created, Created.AddMinutes(5)));
        await AssertConstraintAsync(() => context.SaveChangesAsync(), familyRow ? "FK_TokenFamilies_Accounts" : "FK_RefreshTokens_TokenFamilies");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ParentDeletion_IsBlockedWithoutDeletingSessionEvidence(bool accountRow)
    {
        var family = await FamilyAsync();
        await using var context = database.CreateContext();
        var token = RefreshToken.Create(family.Id, NewDigest(), Created, Created.AddMinutes(5));
        context.Add(token);
        await context.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<SqlException>(() => accountRow
            ? context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [identity].[Accounts] WHERE [Id] = {family.AccountId}")
            : context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [identity].[TokenFamilies] WHERE [Id] = {family.Id}"));
        Assert.Equal(547, error.Number);
        Assert.True(await context.Users.AnyAsync(row => row.Id == family.AccountId));
        Assert.True(await context.Set<TokenFamily>().AnyAsync(row => row.Id == family.Id));
        Assert.True(await context.Set<RefreshToken>().AnyAsync(row => row.Id == token.Id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("SHORT")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("GGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGG")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA ")]
    public async Task DigestConstraint_RejectsNoncanonicalDirectWrites(string digest)
    {
        var family = await FamilyAsync();
        await using var context = database.CreateContext();
        var token = RefreshToken.Create(family.Id, NewDigest(), Created, Created.AddMinutes(5));
        context.Add(token);
        context.Entry(token).Property(row => row.TokenHash).CurrentValue = digest;
        await AssertConstraintAsync(() => context.SaveChangesAsync(), "CK_RefreshTokens_TokenHash");
    }

    [Fact]
    public async Task DigestUniqueIndex_RejectsReuseAcrossDifferentAccountsAndFamilies()
    {
        var first = await FamilyAsync();
        var second = await FamilyAsync();
        var digest = NewDigest();
        await using var context = database.CreateContext();
        context.Add(RefreshToken.Create(first.Id, digest, Created, Created.AddMinutes(5)));
        await context.SaveChangesAsync();
        context.Add(RefreshToken.Create(second.Id, digest, Created, Created.AddMinutes(5)));
        await AssertUniqueAsync(() => context.SaveChangesAsync());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task UnconsumedToken_KeepsUniqueSlotEvenIfExpiredOrFamilyRevoked(bool expired, bool revoked)
    {
        var family = await FamilyAsync();
        await using var context = database.CreateContext();
        var expiry = expired ? Created.AddSeconds(1) : Created.AddHours(1);
        context.Add(RefreshToken.Create(family.Id, NewDigest(), Created, expiry));
        await context.SaveChangesAsync();
        if (revoked)
            await context.Set<TokenFamily>().Where(row => row.Id == family.Id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RevokedAtUtc, Created.AddMinutes(1)));
        context.Add(RefreshToken.Create(family.Id, NewDigest(), Created.AddMinutes(2), Created.AddMinutes(10)));
        await AssertUniqueAsync(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task ConsumedHistory_IsRetainedWhileAReplacementOccupiesTheSlot()
    {
        var family = await FamilyAsync();
        var digest = NewDigest();
        await using var context = database.CreateContext();
        var first = RefreshToken.Create(family.Id, digest, Created, Created.AddMinutes(5));
        context.Add(first);
        await context.SaveChangesAsync();
        await context.Set<RefreshToken>().Where(row => row.Id == first.Id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ConsumedAtUtc, Created.AddMinutes(1)));
        var replacement = RefreshToken.Create(family.Id, NewDigest(), Created.AddMinutes(1), Created.AddMinutes(10));
        context.Add(replacement);
        await context.SaveChangesAsync();
        var history = await context.Set<RefreshToken>().AsNoTracking().Where(row => row.TokenFamilyId == family.Id).ToListAsync();
        Assert.Equal(2, history.Count);
        Assert.Equal(digest, history.Single(row => row.Id == first.Id).TokenHash);
        Assert.Equal(Created.AddMinutes(1), history.Single(row => row.Id == first.Id).ConsumedAtUtc);
        Assert.Equal(replacement.Id, Assert.Single(history, row => row.ConsumedAtUtc is null).Id);
        var error = await Assert.ThrowsAsync<SqlException>(() => context.Set<RefreshToken>().Where(row => row.Id == first.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ConsumedAtUtc, (DateTimeOffset?)null)));
        Assert.Contains(error.Number, new[] { 2601, 2627 });
    }

    [Fact]
    public async Task MultipleFamiliesForOneAccount_AreIndependent()
    {
        var first = await FamilyAsync();
        var second = TokenFamily.Create(first.AccountId, first.SecurityStamp, Created, Created.AddHours(2));
        await using var context = database.CreateContext();
        context.Add(second);
        await context.SaveChangesAsync();
        context.AddRange(RefreshToken.Create(first.Id, NewDigest(), Created, Created.AddMinutes(5)),
            RefreshToken.Create(second.Id, NewDigest(), Created, Created.AddMinutes(5)));
        await context.SaveChangesAsync();
        await context.Set<TokenFamily>().Where(row => row.Id == first.Id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RevokedAtUtc, Created));
        Assert.Null((await context.Set<TokenFamily>().AsNoTracking().SingleAsync(row => row.Id == second.Id)).RevokedAtUtc);
        Assert.Equal(2, await context.Set<RefreshToken>().CountAsync(row => row.TokenFamilyId == first.Id || row.TokenFamilyId == second.Id));
    }

    [Fact]
    public async Task ConcurrentInitialInserts_LeaveExactlyOneUnconsumedToken()
    {
        var family = await FamilyAsync();
        async Task<bool> InsertAsync()
        {
            await using var context = database.CreateContext();
            context.Add(RefreshToken.Create(family.Id, NewDigest(), Created, Created.AddMinutes(5)));
            try
            {
                await context.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException error) when (error.InnerException is SqlException { Number: 2601 or 2627 })
            {
                return false;
            }
        }
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => InsertAsync()));
        Assert.Single(outcomes, outcome => outcome);
        await using var reader = database.CreateContext();
        Assert.Single(await reader.Set<RefreshToken>().Where(row => row.TokenFamilyId == family.Id).ToListAsync());
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(true, -1)]
    [InlineData(false, 0)]
    [InlineData(false, -1)]
    public async Task ExpiryConstraint_RejectsNonpositiveIntervalsBypassingFactories(bool familyRow, int seconds)
    {
        var family = await FamilyAsync();
        await using var context = database.CreateContext();
        if (familyRow)
        {
            context.Attach(family);
            context.Entry(family).Property(row => row.ExpiresAtUtc).CurrentValue = Created.AddSeconds(seconds);
        }
        else
        {
            var token = RefreshToken.Create(family.Id, NewDigest(), Created, Created.AddMinutes(5));
            context.Add(token);
            context.Entry(token).Property(row => row.ExpiresAtUtc).CurrentValue = Created.AddSeconds(seconds);
        }
        await AssertConstraintAsync(() => context.SaveChangesAsync(), familyRow ? "CK_TokenFamilies_Expiry" : "CK_RefreshTokens_Expiry");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StateTimestamp_CannotPrecedeCreation(bool familyRow)
    {
        var family = await FamilyAsync();
        await using var context = database.CreateContext();
        if (familyRow)
        {
            context.Attach(family);
            context.Entry(family).Property(row => row.RevokedAtUtc).CurrentValue = Created.AddTicks(-1);
        }
        else
        {
            var token = RefreshToken.Create(family.Id, NewDigest(), Created, Created.AddMinutes(5));
            context.Add(token);
            context.Entry(token).Property(row => row.ConsumedAtUtc).CurrentValue = Created.AddTicks(-1);
        }
        await AssertConstraintAsync(() => context.SaveChangesAsync(), familyRow ? "CK_TokenFamilies_RevokedAt" : "CK_RefreshTokens_ConsumedAt");
    }

    [Theory]
    [InlineData(true, "CreatedAtUtc")]
    [InlineData(true, "ExpiresAtUtc")]
    [InlineData(true, "RevokedAtUtc")]
    [InlineData(false, "CreatedAtUtc")]
    [InlineData(false, "ExpiresAtUtc")]
    [InlineData(false, "ConsumedAtUtc")]
    public async Task EveryTimestamp_RequiresStoredUtcOffset(bool familyRow, string property)
    {
        var family = await FamilyAsync();
        await using var context = database.CreateContext();
        var value = (property == "ExpiresAtUtc" ? Created.AddHours(1) : Created).ToOffset(TimeSpan.FromHours(5));
        if (familyRow)
        {
            context.Attach(family);
            // Mark explicitly: DateTimeOffset equality compares instants, not their stored offsets.
            var entry = context.Entry(family).Property(property);
            entry.CurrentValue = value;
            entry.IsModified = true;
        }
        else
        {
            var token = RefreshToken.Create(family.Id, NewDigest(), Created, Created.AddMinutes(5));
            context.Add(token);
            context.Entry(token).Property(property).CurrentValue = value;
        }
        await AssertConstraintAsync(() => context.SaveChangesAsync(), familyRow ? "CK_TokenFamilies_Utc" : "CK_RefreshTokens_Utc");
    }

    [Fact]
    public async Task CallerRollback_RemovesNewFamilyAndTokenTogether()
    {
        var existing = await FamilyAsync();
        var family = TokenFamily.Create(existing.AccountId, existing.SecurityStamp, Created, Created.AddHours(1));
        await using (var writer = database.CreateContext())
        {
            await using var transaction = await writer.Database.BeginTransactionAsync();
            writer.Add(family);
            await writer.SaveChangesAsync();
            writer.Add(RefreshToken.Create(family.Id, NewDigest(), Created, Created.AddMinutes(5)));
            await writer.SaveChangesAsync();
            await transaction.RollbackAsync();
        }
        await using var reader = database.CreateContext();
        Assert.False(await reader.Set<TokenFamily>().AnyAsync(row => row.PublicId == family.PublicId));
        Assert.False(await reader.Set<RefreshToken>().AnyAsync(row => row.TokenFamilyId == family.Id));
    }
}

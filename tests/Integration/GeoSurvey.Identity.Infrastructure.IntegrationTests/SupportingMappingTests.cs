using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GeoSurvey.Identity.Infrastructure.IntegrationTests;

public sealed class SupportingMappingTests(IdentityDatabase database) : IClassFixture<IdentityDatabase>
{
    private async Task<IdentityAccount> PersistAccountAsync()
    {
        var account = IdentityAccount.CreatePlatformAccount("user-" + Guid.NewGuid().ToString("N") + "@example.test");
        await using var context = database.CreateContext();
        context.Add(account);
        await context.SaveChangesAsync();
        return account;
    }

    private static object NewDependent(string kind, int accountId) => kind switch
    {
        "claim" => new IdentityUserClaim<int> { UserId = accountId, ClaimType = "test-mapping", ClaimValue = "sample" },
        "login" => new IdentityUserLogin<int> { UserId = accountId, LoginProvider = "test-provider", ProviderKey = Guid.NewGuid().ToString("N") },
        "token" => new IdentityUserToken<int> { UserId = accountId, LoginProvider = "test-provider", Name = Guid.NewGuid().ToString("N") },
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    [Theory]
    [InlineData("claim")]
    [InlineData("login")]
    [InlineData("token")]
    public async Task MissingAccount_IsRejected(string kind)
    {
        await using var context = database.CreateContext();
        context.Add(NewDependent(kind, int.MaxValue));
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(547, Assert.IsType<SqlException>(exception.InnerException).Number);
    }

    [Theory]
    [InlineData("claim")]
    [InlineData("login")]
    [InlineData("token")]
    public async Task AccountDeletion_WithDependentRowIsBlockedBySqlServer(string kind)
    {
        var account = await PersistAccountAsync();
        await using var context = database.CreateContext();
        var dependent = NewDependent(kind, account.Id);
        context.Add(dependent);
        await context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM [identity].[Accounts] WHERE [Id] = {account.Id}"));
        Assert.Equal(547, exception.Number);
        Assert.True(await context.Users.AnyAsync(item => item.Id == account.Id));
        await context.Entry(dependent).ReloadAsync();
        Assert.Equal(EntityState.Unchanged, context.Entry(dependent).State);
    }

    [Theory]
    [InlineData("login", "LoginProvider")]
    [InlineData("login", "ProviderKey")]
    [InlineData("token", "LoginProvider")]
    [InlineData("token", "Name")]
    public async Task SupportingKeys_Accept128CharactersReject129(string kind, string property)
    {
        var account = await PersistAccountAsync();
        await using (var valid = database.CreateContext())
        {
            var dependent = NewDependent(kind, account.Id);
            valid.Add(dependent);
            valid.Entry(dependent).Property(property).CurrentValue = new string('x', 128);
            Assert.Equal(1, await valid.SaveChangesAsync());
        }
        await using var oversized = database.CreateContext();
        var invalid = NewDependent(kind, account.Id);
        oversized.Add(invalid);
        oversized.Entry(invalid).Property(property).CurrentValue = new string('x', 129);
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => oversized.SaveChangesAsync());
        Assert.Contains(Assert.IsType<SqlException>(exception.InnerException).Number, new[] { 2628, 8152 });
    }

    [Theory]
    [InlineData("login")]
    [InlineData("token")]
    public async Task DuplicateCompositeKey_IsRejected(string kind)
    {
        var account = await PersistAccountAsync();
        var dependent = NewDependent(kind, account.Id);
        await using (var writer = database.CreateContext())
        {
            writer.Add(dependent);
            await writer.SaveChangesAsync();
        }
        // A fresh context avoids an EF tracking conflict masking the database constraint.
        await using var duplicate = database.CreateContext();
        duplicate.Add(dependent);
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
        Assert.Contains(Assert.IsType<SqlException>(exception.InnerException).Number, new[] { 2601, 2627 });
    }

    [Fact]
    public async Task SupportingRows_RoundTripThroughIdentityStore()
    {
        var account = await PersistAccountAsync();
        var claim = (IdentityUserClaim<int>)NewDependent("claim", account.Id);
        var login = (IdentityUserLogin<int>)NewDependent("login", account.Id);
        var token = (IdentityUserToken<int>)NewDependent("token", account.Id);
        token.Value = "mapping-sample";
        await using (var writer = database.CreateContext())
        {
            writer.AddRange(claim, login, token);
            await writer.SaveChangesAsync();
        }
        await using var reader = database.CreateContext();
        using var store = new Microsoft.AspNetCore.Identity.EntityFrameworkCore.UserOnlyStore<IdentityAccount, IdentityTestContext, int>(reader);
        Assert.Contains(await store.GetClaimsAsync(account), value => value.Type == claim.ClaimType && value.Value == claim.ClaimValue);
        Assert.Contains(await store.GetLoginsAsync(account), value => value.LoginProvider == login.LoginProvider && value.ProviderKey == login.ProviderKey);
        Assert.Equal(token.Value, await store.GetTokenAsync(account, token.LoginProvider, token.Name, CancellationToken.None));
        Assert.True(await reader.UserTokens.AnyAsync(value => value.UserId == account.Id && value.Name == token.Name));
    }

    [Fact]
    public void SupportingModel_HasReviewedKeysLengthsAndNoActionRelationships()
    {
        using var context = database.CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        foreach (var type in new[] { typeof(IdentityUserClaim<int>), typeof(IdentityUserLogin<int>), typeof(IdentityUserToken<int>) })
        {
            var entity = model.FindEntityType(type)!;
            var foreignKey = Assert.Single(entity.GetForeignKeys());
            Assert.Equal(typeof(IdentityAccount), foreignKey.PrincipalEntityType.ClrType);
            Assert.Equal("UserId", Assert.Single(foreignKey.Properties).Name);
            Assert.True(foreignKey.IsRequired);
            Assert.Equal(DeleteBehavior.NoAction, foreignKey.DeleteBehavior);
        }
        Assert.Equal(new[] { "Id" }, model.FindEntityType(typeof(IdentityUserClaim<int>))!.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal(new[] { "LoginProvider", "ProviderKey" }, model.FindEntityType(typeof(IdentityUserLogin<int>))!.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal(new[] { "UserId", "LoginProvider", "Name" }, model.FindEntityType(typeof(IdentityUserToken<int>))!.FindPrimaryKey()!.Properties.Select(property => property.Name));
        foreach (var type in new[] { typeof(IdentityUserLogin<int>), typeof(IdentityUserToken<int>) })
        {
            foreach (var property in model.FindEntityType(type)!.FindPrimaryKey()!.Properties.Where(property => property.ClrType == typeof(string)))
            {
                Assert.Equal(128, property.GetMaxLength());
                Assert.False(property.IsNullable);
            }
        }
        Assert.Equal(256, model.FindEntityType(typeof(IdentityAccount))!.FindProperty(nameof(IdentityAccount.PhoneNumber))!.GetMaxLength());
        Assert.Null(model.FindEntityType(typeof(IdentityUserPasskey<int>)));
    }
}

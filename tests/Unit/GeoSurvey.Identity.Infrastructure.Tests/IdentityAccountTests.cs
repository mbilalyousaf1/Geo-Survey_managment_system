using GeoSurvey.Identity.Infrastructure;

namespace GeoSurvey.Identity.Infrastructure.Tests;

public sealed class IdentityAccountTests
{
    [Fact]
    public void CreateOrganizationAccount_SetsOrganizationScope()
    {
        var account = IdentityAccount.CreateOrganizationAccount(42, " Alice+field@Example.com ");

        Assert.Equal(AccountScope.Organization, account.AccountScope);
        Assert.Equal(42, account.OrganizationId);
        Assert.Equal("Alice+field@Example.com", account.Email);
        Assert.Equal("ALICE+FIELD@EXAMPLE.COM", account.NormalizedEmail);
        AssertNewIdentifiersAndUnprovisionedCredentials(account);
    }

    [Fact]
    public void CreatePlatformAccount_HasNoOrganization()
    {
        var account = IdentityAccount.CreatePlatformAccount(" Admin@Example.com ");

        Assert.Equal(AccountScope.Platform, account.AccountScope);
        Assert.Null(account.OrganizationId);
        Assert.Equal("Admin@Example.com", account.Email);
        Assert.Equal("ADMIN@EXAMPLE.COM", account.NormalizedEmail);
        AssertNewIdentifiersAndUnprovisionedCredentials(account);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void CreateOrganizationAccount_RejectsNonPositiveOrganizationId(int organizationId)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => IdentityAccount.CreateOrganizationAccount(organizationId, "alice@example.com"));
    }

    [Fact]
    public void SameEmailAcrossAccounts_DoesNotShareIdentifiers()
    {
        var accounts = new[]
        {
            IdentityAccount.CreateOrganizationAccount(1, "alice@example.com"),
            IdentityAccount.CreateOrganizationAccount(2, "alice@example.com"),
            IdentityAccount.CreatePlatformAccount("alice@example.com")
        };

        Assert.Equal(3, accounts.Select(account => account.PublicId).Distinct().Count());
        Assert.Equal(3, accounts.Select(account => account.UserName).Distinct().Count());
        Assert.Single(accounts.Select(account => account.NormalizedEmail).Distinct());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Factories_RejectNullEmail(bool organizationAccount)
    {
        Assert.Throws<ArgumentNullException>(() => CreateAccount(organizationAccount, null!));
    }

    [Theory]
    [InlineData(true, "not-an-email")]
    [InlineData(false, "not-an-email")]
    [InlineData(true, "alice name@example.com")]
    [InlineData(false, "álîce@example.com")]
    public void Factories_ApplyEmailValidation(bool organizationAccount, string email)
    {
        Assert.Throws<ArgumentException>(() => CreateAccount(organizationAccount, email));
    }

    private static IdentityAccount CreateAccount(bool organizationAccount, string email) => organizationAccount
        ? IdentityAccount.CreateOrganizationAccount(1, email)
        : IdentityAccount.CreatePlatformAccount(email);

    private static void AssertNewIdentifiersAndUnprovisionedCredentials(IdentityAccount account)
    {
        Assert.Equal(0, account.Id);
        Assert.NotEqual(Guid.Empty, account.PublicId);
        Assert.Equal(account.PublicId.ToString("N"), account.UserName);
        Assert.Equal(account.PublicId.ToString("N").ToUpperInvariant(), account.NormalizedUserName);
        Assert.Null(account.PasswordHash);
        Assert.False(account.EmailConfirmed);
        Assert.False(account.TwoFactorEnabled);
    }
}

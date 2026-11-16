using GeoSurvey.Organizations.Domain;

namespace GeoSurvey.Organizations.Domain.Tests;

public sealed class OrganizationTests
{
    [Fact]
    public void Create_SetsIdentifiersPreservesCodeAndTrimsName()
    {
        var code = OrganizationCode.Create(" ACME-Surveys ");

        var organization = Organization.Create(code, " \tAcme Surveys\r\n ");

        Assert.Equal(0, organization.Id);
        Assert.NotEqual(Guid.Empty, organization.PublicId);
        Assert.Same(code, organization.Code);
        Assert.Equal("acme-surveys", organization.Code.Value);
        Assert.Equal("Acme Surveys", organization.DisplayName);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(200)]
    public void Create_AcceptsInclusiveDisplayNameBoundariesAfterTrimming(int length)
    {
        var name = new string('a', length);

        var organization = Organization.Create(OrganizationCode.Create("acme"), " " + name + " ");

        Assert.Equal(name, organization.DisplayName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void Create_RejectsEmptyOrWhitespaceName(string name)
    {
        Assert.Throws<ArgumentException>(() => Organization.Create(OrganizationCode.Create("acme"), name));
    }

    [Fact]
    public void Create_Rejects201CharacterName()
    {
        Assert.Throws<ArgumentException>(() => Organization.Create(OrganizationCode.Create("acme"), new string('a', 201)));
    }

    [Fact]
    public void Create_RejectsNullCode()
    {
        Assert.Throws<ArgumentNullException>(() => Organization.Create(null!, "Acme"));
    }

    [Fact]
    public void Create_RejectsNullName()
    {
        Assert.Throws<ArgumentNullException>(() => Organization.Create(OrganizationCode.Create("acme"), null!));
    }

    [Fact]
    public void Create_AllowsDuplicateDisplayNamesForIndependentOrganizations()
    {
        var first = Organization.Create(OrganizationCode.Create("first"), "Survey Team");
        var second = Organization.Create(OrganizationCode.Create("second"), "Survey Team");

        Assert.Equal(first.DisplayName, second.DisplayName);
        Assert.NotEqual(first.PublicId, second.PublicId);
    }

    [Fact]
    public void Create_AssignsUtcTimestampDuringCreation()
    {
        var before = DateTimeOffset.UtcNow;

        var organization = Organization.Create(OrganizationCode.Create("acme"), "Acme");

        var after = DateTimeOffset.UtcNow;
        Assert.Equal(TimeSpan.Zero, organization.CreatedAtUtc.Offset);
        Assert.InRange(organization.CreatedAtUtc, before, after);
    }
}

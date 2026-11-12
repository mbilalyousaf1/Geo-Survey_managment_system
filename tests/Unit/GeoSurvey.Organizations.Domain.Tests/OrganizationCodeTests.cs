using System.Globalization;
using GeoSurvey.Organizations.Domain;

namespace GeoSurvey.Organizations.Domain.Tests;

public sealed class OrganizationCodeTests
{
    [Theory]
    [InlineData(" ACME-Surveys ", "acme-surveys")]
    [InlineData("\tACME-123\r\n", "acme-123")]
    [InlineData("abc", "abc")]
    [InlineData("123", "123")]
    [InlineData("a-1", "a-1")]
    public void Create_NormalizesValidInput(string input, string expected)
    {
        var code = OrganizationCode.Create(input);

        Assert.Equal(expected, code.Value);
        Assert.Equal(expected, code.ToString());
    }

    [Theory]
    [InlineData(3)]
    [InlineData(50)]
    public void Create_AcceptsInclusiveLengthBoundaries(int length)
    {
        Assert.Equal(new string('a', length), OrganizationCode.Create(" " + new string('A', length) + " ").Value);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(51)]
    public void Create_RejectsLengthsOutsideBoundaries(int length)
    {
        Assert.Throws<ArgumentException>(() => OrganizationCode.Create(new string('a', length)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-abc")]
    [InlineData("abc-")]
    [InlineData("ab--cd")]
    [InlineData("---")]
    [InlineData("acme surveys")]
    [InlineData("acme_surveys")]
    [InlineData("acme.surveys")]
    [InlineData("acme/surveys")]
    [InlineData("acme\tsurveys")]
    [InlineData("acme\nsurveys")]
    [InlineData("café")]
    [InlineData("ＡＣＭＥ")]
    [InlineData("acme١")]
    public void Create_RejectsInvalidInput(string input)
    {
        Assert.Throws<ArgumentException>(() => OrganizationCode.Create(input));
    }

    [Fact]
    public void Create_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => OrganizationCode.Create(null!));
    }

    [Fact]
    public void Create_UsesInvariantCasing()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Assert.Equal("istanbul", OrganizationCode.Create("ISTANBUL").Value);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void Equality_UsesNormalizedValue()
    {
        var first = OrganizationCode.Create(" ACME ");
        var second = OrganizationCode.Create("acme");

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, OrganizationCode.Create("other"));
    }
}

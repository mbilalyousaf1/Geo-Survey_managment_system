using System.Globalization;
using GeoSurvey.Identity.Infrastructure;

namespace GeoSurvey.Identity.Infrastructure.Tests;

public sealed class AccountEmailTests
{
    [Theory]
    [InlineData(" Alice.Example+field@Example.com ", "Alice.Example+field@Example.com", "ALICE.EXAMPLE+FIELD@EXAMPLE.COM")]
    [InlineData("\tAlice@Example.com\r\n", "Alice@Example.com", "ALICE@EXAMPLE.COM")]
    [InlineData("\u00a0Alice@Example.com\u00a0", "Alice@Example.com", "ALICE@EXAMPLE.COM")]
    [InlineData("alice@example.com", "alice@example.com", "ALICE@EXAMPLE.COM")]
    public void Create_PreservesTrimmedDisplayAndNormalizesLookup(string input, string display, string normalized)
    {
        var email = AccountEmail.Create(input);

        Assert.Equal(display, email.DisplayValue);
        Assert.Equal(normalized, email.NormalizedValue);
    }

    [Fact]
    public void Create_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => AccountEmail.Create(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    [InlineData("alice example@example.com")]
    [InlineData("alice\t@example.com")]
    [InlineData("alice@exam\nple.com")]
    [InlineData("alice\r@example.com")]
    [InlineData("alice\u00a0@example.com")]
    [InlineData("álîce@example.com")]
    [InlineData("alice@exámple.com")]
    [InlineData("alice@例え.com")]
    [InlineData("alice.example.com")]
    [InlineData("@example.com")]
    [InlineData("alice@")]
    [InlineData("alice@@example.com")]
    public void Create_RejectsInvalidInputWithoutEchoingIt(string input)
    {
        var exception = Assert.Throws<ArgumentException>(() => AccountEmail.Create(input));

        Assert.Equal("value", exception.ParamName);
        if (!string.IsNullOrWhiteSpace(input))
            Assert.DoesNotContain(input, exception.Message);
    }

    [Fact]
    public void Create_Accepts254CharactersAfterTrimming()
    {
        var address = MakeAddress(254);

        var email = AccountEmail.Create(" " + address + " ");

        Assert.Equal(254, email.DisplayValue.Length);
        Assert.Equal(address, email.DisplayValue);
        Assert.Equal(address.ToUpperInvariant(), email.NormalizedValue);
    }

    [Fact]
    public void Create_Rejects255Characters()
    {
        Assert.Throws<ArgumentException>(() => AccountEmail.Create(MakeAddress(255)));
    }

    [Fact]
    public void Create_NormalizesIndependentlyOfCurrentCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Assert.Equal("ILKER@EXAMPLE.COM", AccountEmail.Create("ilker@example.com").NormalizedValue);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void Create_MatchesCasingButDoesNotRemoveDotsOrTags()
    {
        var email = AccountEmail.Create("Alice.Example+field@Example.com");

        Assert.Equal(email.NormalizedValue, AccountEmail.Create("alice.example+FIELD@example.COM").NormalizedValue);
        Assert.NotEqual(email.NormalizedValue, AccountEmail.Create("aliceexample+field@example.com").NormalizedValue);
        Assert.NotEqual(email.NormalizedValue, AccountEmail.Create("alice.example@example.com").NormalizedValue);
    }

    private static string MakeAddress(int length) =>
        new string('a', 64) + "@" + new string('b', 63) + "." + new string('c', 63) + "." + new string('d', length - 193);
}

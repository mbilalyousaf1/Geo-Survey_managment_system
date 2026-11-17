namespace GeoSurvey.Identity.Infrastructure.Tests;

public sealed class BootstrapPasswordTests
{
    [Theory]
    [InlineData(15)]
    [InlineData(128)]
    public void BaseValidation_AcceptsInclusiveAsciiBoundaries(int length)
    {
        var input = new string('a', length);
        Assert.Equal(input, BootstrapPassword.NormalizeAndValidateBase(input));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(14)]
    [InlineData(129)]
    public void BaseValidation_RejectsOutsideAsciiBoundaries(int length)
    {
        Assert.Throws<ArgumentException>(() => BootstrapPassword.NormalizeAndValidateBase(new string('a', length)));
    }

    [Fact]
    public void BaseValidation_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => BootstrapPassword.NormalizeAndValidateBase(null!));
    }

    [Fact]
    public void Normalization_ProducesSameNfcForCanonicallyEquivalentInputs()
    {
        var prefix = new string('x', 14);
        Assert.Equal(prefix + "\u00e9", BootstrapPassword.NormalizeAndValidateBase(prefix + "e\u0301"));
        Assert.Equal(BootstrapPassword.NormalizeAndValidateBase(prefix + "\u00e9"),
            BootstrapPassword.NormalizeAndValidateBase(prefix + "e\u0301"));
    }

    [Theory]
    [InlineData(14, false)]
    [InlineData(15, true)]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public void Length_IsEvaluatedAfterNfcComposition(int normalizedLength, bool accepted)
    {
        var input = string.Concat(Enumerable.Repeat("e\u0301", normalizedLength));
        if (accepted)
            Assert.Equal(new string('\u00e9', normalizedLength), BootstrapPassword.NormalizeAndValidateBase(input));
        else
            Assert.Throws<ArgumentException>(() => BootstrapPassword.NormalizeAndValidateBase(input));
    }

    [Theory]
    [InlineData(14, false)]
    [InlineData(15, true)]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public void Length_CountsSupplementaryCharactersAsOneCodePoint(int codePoints, bool accepted)
    {
        var input = string.Concat(Enumerable.Repeat("\U0001f680", codePoints));
        if (accepted)
            Assert.Equal(input, BootstrapPassword.NormalizeAndValidateBase(input));
        else
            Assert.Throws<ArgumentException>(() => BootstrapPassword.NormalizeAndValidateBase(input));
    }

    [Fact]
    public void BaseValidation_PreservesWhitespaceAndCaseWithoutCompositionRules()
    {
        const string input = "  LowerCase passphrase  ";
        Assert.Equal(input, BootstrapPassword.NormalizeAndValidateBase(input));
        // This tests the base component only, not complete strength/blocklist approval.
        Assert.Equal(new string(' ', 15), BootstrapPassword.NormalizeAndValidateBase(new string(' ', 15)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void InvalidUnicode_IsRejectedWithoutEchoOrInnerParserError(int malformedKind)
    {
        var malformed = malformedKind switch
        {
            0 => "\ud800",
            1 => "\udc00",
            _ => "\ud800x"
        };
        var input = "do-not-echo-input-" + malformed;
        var exception = Assert.Throws<ArgumentException>(() => BootstrapPassword.NormalizeAndValidateBase(input));
        Assert.Contains("valid Unicode", exception.Message);
        Assert.DoesNotContain("do-not-echo-input", exception.ToString());
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void LengthFailure_DoesNotEchoInput()
    {
        var input = "do-not-echo-" + new string('x', 128);
        var exception = Assert.Throws<ArgumentException>(() => BootstrapPassword.NormalizeAndValidateBase(input));
        Assert.DoesNotContain("do-not-echo-", exception.ToString());
    }
}

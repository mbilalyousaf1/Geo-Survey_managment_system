using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace GeoSurvey.Identity.Infrastructure;

/// <summary>Validated email values for display and scoped account lookup; does not establish ownership or uniqueness.</summary>
public sealed class AccountEmail
{
    public string DisplayValue { get; }
    public string NormalizedValue { get; }

    private AccountEmail(string displayValue, string normalizedValue)
    {
        DisplayValue = displayValue;
        NormalizedValue = normalizedValue;
    }

    public static AccountEmail Create(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var trimmed = value.Trim();
        if (trimmed.Length is 0 or > 254)
            throw new ArgumentException("Email must contain 1-254 characters after trimming.", nameof(value));

        foreach (var character in trimmed)
        {
            if (character > 127 || char.IsWhiteSpace(character))
                throw new ArgumentException("Email must contain only ASCII characters and no embedded whitespace.", nameof(value));
        }

        if (!new EmailAddressAttribute().IsValid(trimmed))
            throw new ArgumentException("Email syntax is invalid.", nameof(value));

        var normalized = new UpperInvariantLookupNormalizer().NormalizeEmail(trimmed);
        return new AccountEmail(trimmed, normalized);
    }
}

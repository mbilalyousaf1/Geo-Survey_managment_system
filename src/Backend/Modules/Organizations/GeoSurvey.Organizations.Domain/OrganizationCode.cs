namespace GeoSurvey.Organizations.Domain;

/// <summary>A normalized organization login code. Uniqueness requires persistence enforcement.</summary>
public sealed record OrganizationCode
{
    public string Value { get; }

    private OrganizationCode(string value)
    {
        Value = value;
    }

    public static OrganizationCode Create(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length is < 3 or > 50)
            throw new ArgumentException("Organization code must contain 3-50 characters after normalization.", nameof(value));

        if (normalized[0] == '-' || normalized[^1] == '-' || normalized.Contains("--", StringComparison.Ordinal))
            throw new ArgumentException("Organization code cannot contain leading, trailing, or consecutive hyphens.", nameof(value));

        foreach (var character in normalized)
        {
            if (character is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '-')
                throw new ArgumentException("Organization code permits only English letters, digits, and hyphens.", nameof(value));
        }

        return new OrganizationCode(normalized);
    }

    public override string ToString() => Value;
}

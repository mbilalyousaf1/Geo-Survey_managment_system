using System.Text;

namespace GeoSurvey.Identity.Infrastructure;

/// <summary>Bootstrap NFC normalization and base length checks only; does not check a password blocklist.</summary>
public static class BootstrapPassword
{
    public const int MinimumCodePoints = 15;
    public const int MaximumCodePoints = 128;

    public static string NormalizeAndValidateBase(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        string normalized;
        try
        {
            normalized = password.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            throw new ArgumentException("Password must contain valid Unicode.", nameof(password));
        }

        var codePoints = 0;
        foreach (var rune in normalized.EnumerateRunes())
            codePoints++;

        if (codePoints is < MinimumCodePoints or > MaximumCodePoints)
            throw new ArgumentException("Password must contain 15-128 Unicode code points after NFC normalization.", nameof(password));

        return normalized;
    }
}

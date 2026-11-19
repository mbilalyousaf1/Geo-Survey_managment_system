using System.Text;

namespace GeoSurvey.Identity.Infrastructure.SignIn;

/// <summary>Verification normalization only; creation minimum/blocklist rules are not login rules.</summary>
public static class PlatformSignInPassword
{
    public static string NormalizeForVerification(string password)
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
        var length = 0;
        foreach (var rune in normalized.EnumerateRunes())
        {
            if (++length > BootstrapPassword.MaximumCodePoints)
                throw new ArgumentException("Password exceeds the supported verification length.", nameof(password));
        }
        if (length == 0)
            throw new ArgumentException("Password is required.", nameof(password));
        return normalized;
    }
}

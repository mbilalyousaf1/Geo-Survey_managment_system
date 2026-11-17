using Microsoft.AspNetCore.Identity;

namespace GeoSurvey.Identity.Infrastructure;

/// <summary>Shared hashing settings for future approved credential creation and verification paths.</summary>
public static class IdentityPasswordHashingOptions
{
    public const int IterationCount = 220000;

    public static void Configure(PasswordHasherOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3;
        options.IterationCount = IterationCount;
    }
}

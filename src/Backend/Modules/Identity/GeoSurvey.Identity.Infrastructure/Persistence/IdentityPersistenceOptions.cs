using Microsoft.AspNetCore.Identity;

namespace GeoSurvey.Identity.Infrastructure.Persistence;

/// <summary>Apply consistently before building runtime, test, or design-time Identity models.</summary>
public static class IdentityPersistenceOptions
{
    public const int MaximumKeyLength = 128;

    public static void Configure(IdentityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version2;
        options.Stores.MaxLengthForKeys = MaximumKeyLength;
    }
}

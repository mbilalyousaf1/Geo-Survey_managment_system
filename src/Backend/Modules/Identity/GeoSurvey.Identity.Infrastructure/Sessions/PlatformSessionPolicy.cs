namespace GeoSurvey.Identity.Infrastructure.Sessions;

/// <summary>Approved Platform Administrator limits; organization-user session policy is separate.</summary>
public static class PlatformSessionPolicy
{
    // Reserved for the later JWT issuer; the session starter issues no access token.
    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan RefreshInactivityWindow = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromHours(8);
}

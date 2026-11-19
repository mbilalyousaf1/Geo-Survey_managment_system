namespace GeoSurvey.Identity.Infrastructure.Authentication;

public static class AuthenticationClaims
{
    public const string Subject = "sub";
    public const string Session = "sid";
    public const string Scope = "scope";
    public const string Organization = "organizationId";
    public const string Permission = "permission";
    public const string PlatformPermission = "platform.organizations.manage";
    public const string OrganizationPermission = "organization.admin";
    public const string PlatformPolicy = "PlatformAdministrator";
    public const string OrganizationPolicy = "OrganizationAdministrator";
}

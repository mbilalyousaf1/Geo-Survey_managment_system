using Microsoft.AspNetCore.Identity;

namespace GeoSurvey.Identity.Infrastructure;

/// <summary>Identity storage model. Factories establish initial scope; persistence and account operations must preserve it.</summary>
public sealed class IdentityAccount : IdentityUser<int>
{
    public Guid PublicId { get; private set; }
    public AccountScope AccountScope { get; private set; }
    public int? OrganizationId { get; private set; }

    private IdentityAccount()
    {
    }

    public static IdentityAccount CreateOrganizationAccount(int organizationId, string email)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(organizationId);
        return Create(AccountScope.Organization, organizationId, email);
    }

    public static IdentityAccount CreatePlatformAccount(string email) => Create(AccountScope.Platform, null, email);

    private static IdentityAccount Create(AccountScope scope, int? organizationId, string email)
    {
        var accountEmail = AccountEmail.Create(email);
        var publicId = Guid.NewGuid();
        var userName = publicId.ToString("N");

        return new IdentityAccount
        {
            PublicId = publicId,
            AccountScope = scope,
            OrganizationId = organizationId,
            Email = accountEmail.DisplayValue,
            NormalizedEmail = accountEmail.NormalizedValue,
            UserName = userName,
            NormalizedUserName = new UpperInvariantLookupNormalizer().NormalizeName(userName)
        };
    }
}

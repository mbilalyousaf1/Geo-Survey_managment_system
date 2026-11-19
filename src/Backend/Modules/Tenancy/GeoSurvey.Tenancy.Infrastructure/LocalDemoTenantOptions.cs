using System.Text.RegularExpressions;
using GeoSurvey.Tenancy.Application;
using Microsoft.Data.SqlClient;

namespace GeoSurvey.Tenancy.Infrastructure;

/// <summary>Operator-managed local deployment profile. Never bound from a request DTO.</summary>
public sealed partial class LocalDemoTenantOptions
{
    public const string SectionName = "Tenancy:LocalDemo";
    public string RegionKey { get; set; } = "local-demo";
    public string RegionDisplayName { get; set; } = "Local demonstration";
    public string DatabasePrefix { get; set; } = "GeoSurveyTenant_";
    public string RuntimeConnectionString { get; set; } = "";
    public string? ManagementConnectionString { get; set; }
    public string RuntimeLogin { get; set; } = "GeoSurveyRuntime";

    public bool IsValid(bool requireManagement = false)
    {
        if (RegionKey != "local-demo" || string.IsNullOrWhiteSpace(RegionDisplayName)
            || !SafeIdentifier().IsMatch(DatabasePrefix) || DatabasePrefix.Length > 80
            || !SafeIdentifier().IsMatch(RuntimeLogin))
            return false;
        return ValidConnection(RuntimeConnectionString) && (!requireManagement || ValidConnection(ManagementConnectionString));
    }

    public string DatabaseName(Guid organizationPublicId)
    {
        if (organizationPublicId == Guid.Empty || !IsValid())
            throw new InvalidOperationException("The trusted local tenant profile is invalid.");
        return DatabasePrefix + organizationPublicId.ToString("N");
    }

    public string ConnectionFor(TrustedTenantReference tenant, bool management = false)
    {
        if (tenant.RegionKey != RegionKey || tenant.DatabaseName != DatabaseName(tenant.OrganizationPublicId)
            || (management && !IsValid(true)))
            throw new InvalidOperationException("The tenant registry reference does not match its trusted profile.");
        return new SqlConnectionStringBuilder(management ? ManagementConnectionString! : RuntimeConnectionString)
        {
            InitialCatalog = tenant.DatabaseName,
            PersistSecurityInfo = false,
            ApplicationName = management ? "GeoSurvey.Provisioning" : "GeoSurvey.TenantRuntime"
        }.ConnectionString;
    }

    private static bool ValidConnection(string? value)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var connection = new SqlConnectionStringBuilder(value);
            return !string.IsNullOrWhiteSpace(connection.DataSource) && !string.IsNullOrWhiteSpace(connection.InitialCatalog);
        }
        catch (ArgumentException) { return false; }
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeIdentifier();
}

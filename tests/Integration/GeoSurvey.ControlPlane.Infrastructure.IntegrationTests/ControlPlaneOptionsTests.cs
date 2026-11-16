using GeoSurvey.Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GeoSurvey.ControlPlane.Infrastructure.IntegrationTests;

public sealed class ControlPlaneOptionsTests
{
    // These model/configuration tests never connect to this address and contain no credentials.
    private const string UnusedConnection = "Server=localhost;Database=unused;Integrated Security=True";

    [Theory]
    [InlineData(1, 128)]
    [InlineData(3, 128)]
    [InlineData(2, 64)]
    [InlineData(2, 0)]
    public void Configuration_RejectsUnapprovedIdentitySettings(int schemaVersion, int keyLength)
    {
        using var services = new ServiceCollection().Configure<IdentityOptions>(options =>
        {
            options.Stores.SchemaVersion = new Version(schemaVersion, 0);
            options.Stores.MaxLengthForKeys = keyLength;
        }).BuildServiceProvider();
        var builder = new DbContextOptionsBuilder<GlobalControlPlaneDbContext>();
        var exception = Assert.Throws<InvalidOperationException>(() => builder.UseGeoSurveyControlPlaneSqlServer(UnusedConnection, services));
        Assert.Contains("IdentityPersistenceOptions", exception.Message);
    }

    [Fact]
    public void Context_RejectsUnapprovedSettingsEvenWhenConfigurationHelperIsBypassed()
    {
        using var services = new ServiceCollection().Configure<IdentityOptions>(options =>
        {
            IdentityPersistenceOptions.Configure(options);
            options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
        }).BuildServiceProvider();
        var options = new DbContextOptionsBuilder<GlobalControlPlaneDbContext>()
            .UseApplicationServiceProvider(services).UseSqlServer(UnusedConnection).Options;
        Assert.Throws<InvalidOperationException>(() => new GlobalControlPlaneDbContext(options));
    }
}

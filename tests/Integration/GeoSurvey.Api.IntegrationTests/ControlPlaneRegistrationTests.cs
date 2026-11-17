using System.Data;
using GeoSurvey.ControlPlane.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GeoSurvey.Api.IntegrationTests;

public sealed class ControlPlaneRegistrationTests : IClassFixture<ConfiguredApiFactory>
{
    private readonly ConfiguredApiFactory _factory;

    public ControlPlaneRegistrationTests(ConfiguredApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Context_IsScopedAndConfigurationDoesNotConnectOrMigrate()
    {
        await using var firstScope = _factory.Services.CreateAsyncScope();
        await using var secondScope = _factory.Services.CreateAsyncScope();
        var first = firstScope.ServiceProvider.GetRequiredService<GlobalControlPlaneDbContext>();
        Assert.Same(first, firstScope.ServiceProvider.GetRequiredService<GlobalControlPlaneDbContext>());
        var second = secondScope.ServiceProvider.GetRequiredService<GlobalControlPlaneDbContext>();
        Assert.NotSame(first, second);
        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", first.Database.ProviderName);
        Assert.Equal(9, first.Model.GetEntityTypes().Count());
        Assert.Equal(ConnectionState.Closed, first.Database.GetDbConnection().State);
        Assert.Equal(ConnectionState.Closed, second.Database.GetDbConnection().State);
        Assert.Contains("[controlplane].[__EFMigrationsHistory]", first.GetService<IHistoryRepository>().GetCreateScript());
        var identity = firstScope.ServiceProvider.GetRequiredService<IOptions<IdentityOptions>>().Value;
        Assert.Equal(IdentitySchemaVersions.Version2, identity.Stores.SchemaVersion);
        Assert.Equal(128, identity.Stores.MaxLengthForKeys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("invalid-do-not-echo")]
    [InlineData("Server=127.0.0.1;Database=Test;Password=do-not-echo;Encrypt=invalid-value")]
    [InlineData("Server=127.0.0.1")]
    [InlineData("Database=Test")]
    public void InvalidConnection_BlocksStartupWithoutEchoingInput(string? connection)
    {
        using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:GlobalControlPlane"] = connection
            })));
        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        Assert.Contains("ConnectionStrings:GlobalControlPlane", exception.Message);
        Assert.DoesNotContain("do-not-echo", exception.ToString());
        Assert.DoesNotContain("invalid-value", exception.ToString());
        if (!string.IsNullOrWhiteSpace(connection))
            Assert.DoesNotContain(connection, exception.ToString());
    }
}

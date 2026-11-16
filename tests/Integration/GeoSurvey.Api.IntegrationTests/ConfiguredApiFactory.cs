using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace GeoSurvey.Api.IntegrationTests;

public sealed class ConfiguredApiFactory : WebApplicationFactory<Program>
{
    // Explicit test configuration, no credentials or reachable server. Never an application fallback.
    public const string OfflineConnection = "Server=127.0.0.1,1;Database=GeoSurvey_ApiTests;Integrated Security=True;Encrypt=True;Connect Timeout=1";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:GlobalControlPlane"] = OfflineConnection }));
    }
}

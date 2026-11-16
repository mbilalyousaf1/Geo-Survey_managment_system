using System.Net;

namespace GeoSurvey.Api.IntegrationTests;

public sealed class LivenessTests : IClassFixture<ConfiguredApiFactory>
{
    private readonly ConfiguredApiFactory _factory;

    public LivenessTests(ConfiguredApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetLiveness_ReturnsHealthyWithoutCaching()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
        Assert.True(response.Headers.CacheControl?.NoStore);
    }
}

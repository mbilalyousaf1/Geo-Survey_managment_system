using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GeoSurvey.Api.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GeoSurvey.Api.IntegrationTests;

public sealed class AuthenticationHttpSecurityTests(ConfiguredApiFactory factory) : IClassFixture<ConfiguredApiFactory>
{
    private HttpClient Client() => factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    [Fact]
    public async Task MissingCsrf_IsDeniedBeforeCredentialLookup_WithoutEchoingInput()
    {
        using var client = Client();
        using var response = await client.PostAsJsonAsync("/api/v1/auth/platform/password", new { email = "do-not-echo@example.test", password = "do-not-echo-password" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.DoesNotContain("do-not-echo", await response.Content.ReadAsStringAsync());
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task CsrfCookie_IsHostOnlySecureHttpOnlyStrict_AndInvalidProofHasSafeError()
    {
        using var client = Client();
        using var csrf = await client.GetAsync("/api/v1/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, csrf.StatusCode);
        Assert.True(csrf.Headers.CacheControl?.NoStore);
        var cookie = Assert.Single(csrf.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("__Host-GeoSurvey.Antiforgery=", cookie);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
        using var json = JsonDocument.Parse(await csrf.Content.ReadAsStringAsync());
        var token = json.RootElement.GetProperty("data").GetProperty("requestToken").GetString();
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token);
        using var proof = await client.PostAsJsonAsync("/api/v1/auth/platform/enrollment", new { challengeToken = "invalid-do-not-echo" });
        Assert.Equal(HttpStatusCode.Unauthorized, proof.StatusCode);
        Assert.DoesNotContain("invalid-do-not-echo", await proof.Content.ReadAsStringAsync());
        Assert.Equal("Bearer", proof.Headers.WwwAuthenticate.Single().Scheme);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", "tampered");
        using var tampered = await client.PostAsJsonAsync("/api/v1/auth/platform/enrollment", new { challengeToken = "invalid" });
        Assert.Equal(HttpStatusCode.BadRequest, tampered.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/auth/me")]
    [InlineData("/api/v1/platform/organizations")]
    [InlineData("/api/v1/tenant/setup")]
    public async Task ProtectedRoutes_RejectAnonymousAndMalformedBearer(string path)
    {
        using var client = Client();
        using var anonymous = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "malformed-do-not-echo");
        using var malformed = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, malformed.StatusCode);
        Assert.DoesNotContain("do-not-echo", await malformed.Content.ReadAsStringAsync());
        Assert.Equal("Bearer", malformed.Headers.WwwAuthenticate.Single().Scheme);
    }

    [Fact]
    public async Task SigningKeyCannotReuseDataProtectionKey_AndFailureDoesNotExposeSecrets()
    {
        using var material = new TestKeyMaterial();
        var settings = new Dictionary<string, string?>(material.Settings)
        {
            ["Authentication:SigningCertificatePath"] = material.Settings["DataProtection:CertificatePath"],
            ["Authentication:SigningCertificatePassword"] = material.Settings["DataProtection:CertificatePassword"]
        };
        using var host = new HostBuilder().ConfigureAppConfiguration(config => config.AddInMemoryCollection(settings))
            .ConfigureServices((context, services) => services.AddGeoSurveyAuthentication(context.Configuration, context.HostingEnvironment)).Build();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(material.Settings["DataProtection:CertificatePassword"]!, error.ToString());
    }

    [Fact]
    public async Task OpenApi_ContainsVersionedAuthenticationAndResourceRoutes()
    {
        using var client = Client();
        using var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = json.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/v1/auth/platform/password", out _));
        Assert.True(paths.TryGetProperty("/api/v1/auth/platform/recovery", out _));
        Assert.True(paths.TryGetProperty("/api/v1/auth/organization/refresh", out _));
        Assert.True(paths.TryGetProperty("/api/v1/tenant/setup", out _));
    }
}

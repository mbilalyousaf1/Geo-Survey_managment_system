using System.Buffers.Binary;
using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using GeoSurvey.ControlPlane.Infrastructure;
using GeoSurvey.ControlPlane.Infrastructure.Bootstrap;
using GeoSurvey.Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeoSurvey.Api.IntegrationTests;

/// <summary>Real SQL, password hashing, protected enrollment, TOTP, RSA JWT and HTTP cookies. No auth-service substitutes.</summary>
public sealed class PlatformAuthenticationAcceptanceTests
{
    [Fact]
    public async Task BootstrapEnrollmentTotpRecoveryRotationAndLogout_EnforceActualPublicFlow()
    {
        await using var database = await AuthenticationDatabase.CreateAsync();
        using var factory = new SqlApiFactory(database.ConnectionString);
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = false, AllowAutoRedirect = false });
        var browser = new Browser(client);
        await browser.CsrfAsync();
        const string email = "synthetic-platform-http@example.test";
        const string password = "Synthetic platform acceptance password 7381";
        Assert.Equal(BootstrapCreationResult.Created, await new FirstPlatformAdministratorBootstrapper(database.OperatorOptions()).CreateAsync(email, password));
        using var passwordEntry = await browser.PostAsync("platform/password", new { email, password });
        Assert.Equal(HttpStatusCode.OK, passwordEntry.StatusCode);
        var challenge = (await DataAsync(passwordEntry)).GetProperty("challengeToken").GetString();
        using var materialResponse = await browser.PostAsync("platform/enrollment", new { challengeToken = challenge });
        Assert.Equal(HttpStatusCode.OK, materialResponse.StatusCode);
        var secret = (await DataAsync(materialResponse)).GetProperty("secret").GetString()!;
        var enrolledStep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        using var confirmation = await browser.PostAsync("platform/enrollment/confirm", new { challengeToken = challenge, code = Code(secret, enrolledStep) });
        Assert.Equal(HttpStatusCode.OK, confirmation.StatusCode);
        var backups = (await DataAsync(confirmation)).GetProperty("recoveryCodes").EnumerateArray().Select(code => code.GetString()!).ToArray();
        Assert.Equal(10, backups.Length);
        using var confirmedReplay = await browser.PostAsync("platform/enrollment/confirm", new { challengeToken = challenge, code = Code(secret, enrolledStep) });
        Assert.Equal(HttpStatusCode.Unauthorized, confirmedReplay.StatusCode);
        using var nextPassword = await browser.PostAsync("platform/password", new { email, password });
        var verification = (await DataAsync(nextPassword)).GetProperty("challengeToken").GetString();
        using var login = await browser.PostAsync("platform/verify", new { challengeToken = verification, code = Code(secret, enrolledStep + 1) });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var signedIn = await DataAsync(login);
        var accessToken = signedIn.GetProperty("accessToken").GetString()!;
        var session = signedIn.GetProperty("sessionPublicId").GetGuid();
        Assert.Equal("Platform", signedIn.GetProperty("user").GetProperty("scope").GetString());
        Assert.False(signedIn.TryGetProperty("refreshToken", out _));
        var setCookie = login.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("__Host-GeoSurvey.Platform.Refresh=", StringComparison.Ordinal));
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
        var originalRefresh = browser.Cookies["__Host-GeoSurvey.Platform.Refresh"];
        using var me = await browser.GetAuthorizedAsync("/api/v1/auth/me", accessToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var tenantDenied = await browser.GetAuthorizedAsync("/api/v1/tenant/setup", accessToken);
        Assert.Equal(HttpStatusCode.Forbidden, tenantDenied.StatusCode);
        using var mismatch = await browser.PostAsync("platform/refresh", new { expectedSessionPublicId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
        using var rotation = await browser.PostAsync("platform/refresh", new { expectedSessionPublicId = session });
        Assert.Equal(HttpStatusCode.OK, rotation.StatusCode);
        var latestRefresh = browser.Cookies["__Host-GeoSurvey.Platform.Refresh"];
        Assert.True(originalRefresh != latestRefresh);
        browser.Cookies["__Host-GeoSurvey.Platform.Refresh"] = originalRefresh;
        using var reuse = await browser.PostAsync("platform/refresh", new { expectedSessionPublicId = session });
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
        using var immediatelyRevoked = await browser.GetAuthorizedAsync("/api/v1/auth/me", accessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, immediatelyRevoked.StatusCode);
        using var recoveryPassword = await browser.PostAsync("platform/password", new { email, password });
        var recoveryChallenge = (await DataAsync(recoveryPassword)).GetProperty("challengeToken").GetString();
        using var recovery = await browser.PostAsync("platform/recovery", new { challengeToken = recoveryChallenge, recoveryCode = backups[0].ToLowerInvariant() });
        Assert.Equal(HttpStatusCode.OK, recovery.StatusCode);
        var recoveredAccess = (await DataAsync(recovery)).GetProperty("accessToken").GetString()!;
        using var recoveredMe = await browser.GetAuthorizedAsync("/api/v1/auth/me", recoveredAccess);
        Assert.Equal(HttpStatusCode.OK, recoveredMe.StatusCode);
        using var logout = await browser.PostAsync("platform/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var revoked = await browser.GetAuthorizedAsync("/api/v1/auth/me", recoveredAccess);
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        using var usedPassword = await browser.PostAsync("platform/password", new { email, password });
        var usedChallenge = (await DataAsync(usedPassword)).GetProperty("challengeToken").GetString();
        using var usedBackup = await browser.PostAsync("platform/recovery", new { challengeToken = usedChallenge, recoveryCode = backups[0] });
        Assert.Equal(HttpStatusCode.Unauthorized, usedBackup.StatusCode);
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("data").Clone();
    }

    private sealed class Browser(HttpClient client)
    {
        public Dictionary<string, string> Cookies { get; } = new(StringComparer.Ordinal);
        private string? _csrf;
        public async Task CsrfAsync()
        {
            using var response = await client.GetAsync("/api/v1/auth/csrf");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Remember(response);
            _csrf = (await DataAsync(response)).GetProperty("requestToken").GetString();
        }
        public async Task<HttpResponseMessage> PostAsync(string path, object? body)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/" + path);
            if (body is not null) request.Content = JsonContent.Create(body);
            request.Headers.Add("X-CSRF-TOKEN", _csrf);
            request.Headers.Add("Cookie", string.Join("; ", Cookies.Select(cookie => cookie.Key + "=" + cookie.Value)));
            var response = await client.SendAsync(request);
            Remember(response);
            return response;
        }
        public Task<HttpResponseMessage> GetAuthorizedAsync(string path, string accessToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            return SendAndDisposeAsync(request);
        }
        private async Task<HttpResponseMessage> SendAndDisposeAsync(HttpRequestMessage request)
        { using (request) return await client.SendAsync(request); }
        private void Remember(HttpResponseMessage response)
        {
            if (!response.Headers.TryGetValues("Set-Cookie", out var values)) return;
            foreach (var value in values)
            {
                var pair = value.Split(';', 2)[0].Split('=', 2);
                if (pair[1].Length == 0) Cookies.Remove(pair[0]); else Cookies[pair[0]] = pair[1];
            }
        }
    }

    private static string Code(string secret, long step)
    {
        Span<byte> key = stackalloc byte[20];
        var buffer = 0; var bits = 0; var position = 0;
        foreach (var character in secret)
        {
            buffer = (buffer << 5) | "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".IndexOf(character);
            bits += 5;
            if (bits >= 8) { bits -= 8; key[position++] = (byte)(buffer >> bits); }
            buffer &= (1 << bits) - 1;
        }
        Span<byte> counter = stackalloc byte[8];
        Span<byte> hash = stackalloc byte[20];
        try
        {
            BinaryPrimitives.WriteInt64BigEndian(counter, step);
            HMACSHA1.HashData(key, counter, hash);
            var offset = hash[^1] & 15;
            return ((BinaryPrimitives.ReadUInt32BigEndian(hash.Slice(offset, 4)) & 0x7fffffff) % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(counter); CryptographicOperations.ZeroMemory(hash); }
    }

    private sealed class SqlApiFactory(string connection) : WebApplicationFactory<Program>
    {
        private readonly TestKeyMaterial _keys = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>(_keys.Settings)
            { ["ConnectionStrings:GlobalControlPlane"] = connection, ["Tenancy:LocalDemo:RuntimeConnectionString"] = connection }));
        }
        protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) _keys.Dispose(); }
    }

    private sealed class AuthenticationDatabase : IAsyncDisposable
    {
        private readonly ServiceProvider _services = new ServiceCollection().Configure<IdentityOptions>(IdentityPersistenceOptions.Configure).BuildServiceProvider();
        public string ConnectionString { get; private set; } = null!;
        private bool _created;
        public static async Task<AuthenticationDatabase> CreateAsync()
        {
            var database = new AuthenticationDatabase();
            try
            {
                var supplied = Environment.GetEnvironmentVariable("GEOSURVEY_TEST_SQLSERVER");
                if (string.IsNullOrWhiteSpace(supplied)) throw new InvalidOperationException("Run scripts/Test-Backend.ps1 to provide isolated SQL Server for HTTP acceptance.");
                var name = "GeoSurvey_AuthHttpTests_" + Guid.NewGuid().ToString("N");
                var builder = new SqlConnectionStringBuilder(supplied) { InitialCatalog = "master", Pooling = false, ConnectTimeout = 2 };
                var ready = false;
                for (var attempt = 0; attempt < 60; attempt++)
                {
                    try
                    {
                        await using var connection = new SqlConnection(builder.ConnectionString);
                        await connection.OpenAsync();
                        await using var probe = new SqlCommand("SELECT DB_ID(@name)", connection);
                        probe.Parameters.AddWithValue("@name", name);
                        if (await probe.ExecuteScalarAsync() is not DBNull)
                            throw new InvalidOperationException("The isolated authentication target unexpectedly exists.");
                        ready = true;
                        break;
                    }
                    catch (SqlException) { await Task.Delay(TimeSpan.FromSeconds(2)); }
                }
                if (!ready) throw new InvalidOperationException("Isolated SQL Server did not become ready for HTTP authentication acceptance.");
                builder.InitialCatalog = name;
                database.ConnectionString = builder.ConnectionString;
                await using var context = database.Context();
                database._created = true;
                await context.Database.MigrateAsync();
                await context.Database.ExecuteSqlRawAsync("IF DATABASE_PRINCIPAL_ID(N'GeoSurveyBootstrap') IS NULL EXEC(N'CREATE ROLE [GeoSurveyBootstrap] AUTHORIZATION [dbo]'); CREATE USER [AuthHttpBootstrap] WITHOUT LOGIN; ALTER ROLE [GeoSurveyBootstrap] ADD MEMBER [AuthHttpBootstrap]; ALTER ROLE [db_owner] ADD MEMBER [AuthHttpBootstrap];");
                return database;
            }
            catch { await database.DisposeAsync(); throw; }
        }
        private GlobalControlPlaneDbContext Context() => new(new DbContextOptionsBuilder<GlobalControlPlaneDbContext>()
            .UseGeoSurveyControlPlaneSqlServer(ConnectionString, _services).Options);
        public DbContextOptions<GlobalControlPlaneDbContext> OperatorOptions() => new DbContextOptionsBuilder<GlobalControlPlaneDbContext>()
            .UseGeoSurveyControlPlaneSqlServer(ConnectionString, _services).AddInterceptors(new BootstrapUser()).Options;
        public async ValueTask DisposeAsync()
        {
            try { if (_created) { await using var context = Context(); await context.Database.EnsureDeletedAsync(); } }
            finally { await _services.DisposeAsync(); }
        }
        private sealed class BootstrapUser : DbConnectionInterceptor
        {
            public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
            { await using var command = connection.CreateCommand(); command.CommandText = "EXECUTE AS USER = N'AuthHttpBootstrap';"; await command.ExecuteNonQueryAsync(cancellationToken); }
        }
    }
}

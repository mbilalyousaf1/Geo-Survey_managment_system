using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using GeoSurvey.ControlPlane.Infrastructure.Authentication;
using GeoSurvey.ControlPlane.Infrastructure.SignIn;
using GeoSurvey.Identity.Application.Authentication;
using GeoSurvey.Identity.Infrastructure.Authentication;
using GeoSurvey.Identity.Infrastructure.SignIn;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

namespace GeoSurvey.Api.Authentication;

public static class AuthenticationRegistration
{
    public static IServiceCollection AddGeoSurveyAuthentication(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddOptions<JwtSigningOptions>().Bind(configuration.GetSection("Authentication"))
            .Validate(options => Uri.TryCreate(options.Issuer, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps,
                "Authentication:Issuer must be an absolute HTTPS issuer.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Audience), "Authentication:Audience is required.")
            .Validate(options => ExternalCertificate(options.SigningCertificatePath, environment.ContentRootPath),
                "Authentication:SigningCertificatePath must be a readable absolute PFX path outside the application/repository.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.SigningCertificatePassword),
                "Authentication:SigningCertificatePassword must be supplied through secret configuration.")
            .ValidateOnStart();
        services.AddSingleton<JwtSigningCertificate>();
        services.AddSingleton<IAccessTokenIssuer>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<JwtSigningOptions>>().Value;
            return new RsaAccessTokenIssuer(provider.GetRequiredService<JwtSigningCertificate>().Key,
                options.Issuer!, options.Audience!, provider.GetRequiredService<TimeProvider>());
        });
        services.AddHostedService<JwtSigningStartupCheck>();
        services.AddScoped<IAuthenticationSessions, AuthenticationSessions>();
        services.AddScoped<IPlatformEnrollment, PlatformEnrollmentService>();
        services.AddScoped<IPlatformPasswordEntry, PlatformPasswordEntryService>();
        services.AddScoped<IPlatformSessionStarter, PlatformSessionStarter>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtSigningOptions>, JwtSigningCertificate>((bearer, settings, certificate) =>
            {
                bearer.MapInboundClaims = false;
                bearer.IncludeErrorDetails = false;
                bearer.RequireHttpsMetadata = true;
                bearer.SaveToken = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = settings.Value.Issuer,
                    ValidateAudience = true,
                    ValidAudience = settings.Value.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = certificate.Key,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ClockSkew = TimeSpan.Zero,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    ValidTypes = ["at+jwt"],
                    NameClaimType = AuthenticationClaims.Subject,
                    LogValidationExceptions = false
                };
                bearer.Events = new JwtBearerEvents
                {
                    OnTokenValidated = ValidateCurrentSessionAsync,
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.Headers.CacheControl = "no-store";
                        context.Response.Headers.WWWAuthenticate = "Bearer";
                        await Results.Problem(statusCode: 401, title: "Authentication required.").ExecuteAsync(context.HttpContext);
                    },
                    OnForbidden = context =>
                    {
                        context.Response.Headers.CacheControl = "no-store";
                        return Results.Problem(statusCode: 403, title: "Access is not permitted.").ExecuteAsync(context.HttpContext);
                    }
                };
            });
        services.AddAuthorizationBuilder()
            .AddPolicy(AuthenticationClaims.PlatformPolicy, policy => policy.RequireAuthenticatedUser()
                .RequireClaim(AuthenticationClaims.Scope, "Platform").RequireClaim(AuthenticationClaims.Permission, AuthenticationClaims.PlatformPermission))
            .AddPolicy(AuthenticationClaims.OrganizationPolicy, policy => policy.RequireAuthenticatedUser()
                .RequireClaim(AuthenticationClaims.Scope, "Organization").RequireClaim(AuthenticationClaims.Permission, AuthenticationClaims.OrganizationPermission));
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = "__Host-GeoSurvey.Antiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.Path = "/";
        });
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("authentication", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
            options.OnRejected = async (context, _) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = "60";
                await Results.Problem(statusCode: 429, title: "Too many authentication requests. Try again shortly.").ExecuteAsync(context.HttpContext);
            };
        });
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "GeoSurvey Phase 1 API";
                document.Info.Description = "First-party HTTPS authentication and isolated organization provisioning. Obtain a CSRF requestToken from GET /api/v1/auth/csrf, retain its host cookie, and send X-CSRF-TOKEN for unsafe authentication/activation requests. Access tokens remain in memory; refresh tokens are HttpOnly host cookies.";
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
                { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", Description = "RS256 access token. Current account/session and tenant grants are checked on every authenticated request." };
                document.Components.SecuritySchemes["Csrf"] = new OpenApiSecurityScheme
                { Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Header, Name = "X-CSRF-TOKEN", Description = "The requestToken returned by /api/v1/auth/csrf, with its matching browser cookie." };
                return Task.CompletedTask;
            });
            options.AddOperationTransformer((operation, context, _) =>
            {
                var metadata = context.Description.ActionDescriptor.EndpointMetadata;
                if (metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any())
                    operation.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = [] }];
                else if (context.Description.HttpMethod == "POST" && (context.Description.RelativePath?.StartsWith("api/v1/auth/", StringComparison.Ordinal) == true
                    || context.Description.RelativePath?.StartsWith("api/v1/activation/", StringComparison.Ordinal) == true))
                    operation.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Csrf", context.Document)] = [] }];
                return Task.CompletedTask;
            });
        });
        return services;
    }

    /// <summary>Call after routing and before mapping resource endpoints. The API has a single HTTPS browser origin.</summary>
    public static IApplicationBuilder UseGeoSurveyAuthentication(this IApplicationBuilder app) =>
        app.UseRateLimiter().UseAuthentication().UseAuthorization();

    public static SessionSubject? CurrentSession(this HttpContext context) => context.Items[typeof(SessionSubject)] as SessionSubject;

    private static async Task ValidateCurrentSessionAsync(TokenValidatedContext context)
    {
        var principal = context.Principal!;
        static string? Single(System.Security.Claims.ClaimsPrincipal principal, string type)
        {
            var values = principal.FindAll(type).Take(2).ToArray();
            return values.Length == 1 ? values[0].Value : null;
        }
        var scopeValue = Single(principal, AuthenticationClaims.Scope);
        if (!Guid.TryParseExact(Single(principal, AuthenticationClaims.Subject), "D", out var account)
            || !Guid.TryParseExact(Single(principal, AuthenticationClaims.Session), "D", out var session)
            || scopeValue is not ("Platform" or "Organization"))
        { context.Fail("Invalid authentication subject."); return; }
        var scope = scopeValue == "Platform" ? AuthenticationScope.Platform : AuthenticationScope.Organization;
        var organizationValue = Single(principal, AuthenticationClaims.Organization);
        Guid? organization = null;
        if (scope == AuthenticationScope.Organization)
        {
            if (!Guid.TryParseExact(organizationValue, "D", out var parsed))
            { context.Fail("Invalid organization binding."); return; }
            organization = parsed;
        }
        else if (principal.HasClaim(claim => claim.Type == AuthenticationClaims.Organization))
        { context.Fail("Invalid platform binding."); return; }
        var expectedPermission = scope == AuthenticationScope.Platform ? AuthenticationClaims.PlatformPermission : AuthenticationClaims.OrganizationPermission;
        if (Single(principal, AuthenticationClaims.Permission) != expectedPermission)
        { context.Fail("Invalid authorization binding."); return; }
        try
        {
            var current = await context.HttpContext.RequestServices.GetRequiredService<IAuthenticationSessions>()
                .ValidateAsync(account, session, scope, organization, context.HttpContext.RequestAborted);
            if (current is null)
                context.Fail("The session is no longer available.");
            else
                context.HttpContext.Items[typeof(SessionSubject)] = current;
        }
        catch (InvalidOperationException)
        { context.Fail("The session could not be validated."); }
    }

    private static bool ExternalCertificate(string? path, string contentRoot)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || !File.Exists(path))
            return false;
        try
        {
            var full = Path.GetFullPath(path);
            for (var directory = new DirectoryInfo(contentRoot); directory is not null; directory = directory.Parent)
            {
                if (directory.FullName != Path.GetFullPath(contentRoot) && !Directory.Exists(Path.Combine(directory.FullName, ".git")))
                    continue;
                var relative = Path.GetRelativePath(directory.FullName, full);
                if (relative == "." || (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
                    return false;
            }
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException) { return false; }
    }
}

internal sealed class JwtSigningOptions
{
    public string? Issuer { get; set; }
    public string? Audience { get; set; }
    public string? SigningCertificatePath { get; set; }
    public string? SigningCertificatePassword { get; set; }
}

internal sealed class JwtSigningCertificate : IDisposable
{
    private readonly X509Certificate2 _certificate;
    public SecurityKey Key { get; }

    public JwtSigningCertificate(IOptions<JwtSigningOptions> settings, IConfiguration configuration)
    {
        X509Certificate2? loaded = null;
        try
        {
            var options = settings.Value;
            loaded = X509CertificateLoader.LoadPkcs12FromFile(options.SigningCertificatePath!, options.SigningCertificatePassword, X509KeyStorageFlags.EphemeralKeySet);
            using var signingKey = loaded.GetRSAPrivateKey();
            if (signingKey is null || signingKey.KeySize < 2048 || loaded.NotBefore.ToUniversalTime() > DateTime.UtcNow || loaded.NotAfter.ToUniversalTime() <= DateTime.UtcNow)
                throw new CryptographicException();
            var dpPath = configuration["DataProtection:CertificatePath"];
            if (!string.IsNullOrWhiteSpace(dpPath))
            {
                using var dataProtection = X509CertificateLoader.LoadPkcs12FromFile(dpPath, configuration["DataProtection:CertificatePassword"], X509KeyStorageFlags.EphemeralKeySet);
                using var protectionKey = dataProtection.GetRSAPublicKey();
                if (protectionKey is not null && CryptographicOperations.FixedTimeEquals(signingKey.ExportSubjectPublicKeyInfo(), protectionKey.ExportSubjectPublicKeyInfo()))
                    throw new CryptographicException();
            }
            _certificate = loaded;
            Key = new X509SecurityKey(_certificate);
        }
        catch (Exception exception) when (exception is CryptographicException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            loaded?.Dispose();
            throw new InvalidOperationException("JWT signing requires its own readable, currently valid RSA PFX (at least 2048 bits), separate from Data Protection, and the correct secret password.");
        }
    }
    public void Dispose() => _certificate.Dispose();
}

internal sealed class JwtSigningStartupCheck(JwtSigningCertificate certificate) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); _ = certificate.Key; return Task.CompletedTask; }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

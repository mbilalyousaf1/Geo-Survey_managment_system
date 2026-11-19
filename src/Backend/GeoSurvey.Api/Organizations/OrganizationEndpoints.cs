using System.Security.Claims;
using GeoSurvey.Organizations.Application;
using GeoSurvey.Tenancy.Application;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GeoSurvey.Api.Organizations;

public static class OrganizationEndpoints
{
    public static IEndpointRouteBuilder MapPhaseOneOrganizations(this IEndpointRouteBuilder endpoints)
    {
        var platform = endpoints.MapGroup("/api/v1/platform").WithTags("Platform organizations")
            .RequireAuthorization("PlatformAdministrator").AddEndpointFilter<OrganizationExceptionFilter>();
        platform.MapGet("/regions", (IOrganizationManagement organizations) => Results.Ok(new { data = organizations.GetRegions() }))
            .WithSummary("List the server-managed organization regions.")
            .Produces<ApiResponse<IReadOnlyList<RegionDto>>>();
        platform.MapGet("/organizations", async (int? pageNumber, int? pageSize, IOrganizationManagement organizations, CancellationToken ct) =>
            Results.Ok(new { data = await organizations.ListAsync(pageNumber ?? 1, pageSize ?? 25, ct) }))
            .WithSummary("List organizations and their current provisioning status.")
            .Produces<ApiResponse<OrganizationPageDto>>().ProducesProblem(400).ProducesProblem(503);
        platform.MapGet("/organizations/{publicId:guid}", async (Guid publicId, IOrganizationManagement organizations, CancellationToken ct) =>
        {
            var organization = await organizations.GetAsync(publicId, ct);
            return organization is null ? Results.Problem(statusCode: 404, title: "Organization was not found.",
                extensions: new Dictionary<string, object?> { ["code"] = "organization-not-found" }) : Results.Ok(new { data = organization });
        }).WithSummary("Read one organization's provisioning status.")
            .Produces<ApiResponse<OrganizationDto>>().ProducesProblem(404).ProducesProblem(503);
        platform.MapPost("/organizations", async (CreateOrganizationRequest request, HttpContext http, IOrganizationManagement organizations, CancellationToken ct) =>
        {
            var organization = await organizations.CreateAsync(request, Subject(http.User), ct);
            return Results.Created($"/api/v1/platform/organizations/{organization.PublicId:D}", new { data = organization });
        }).WithSummary("Create an organization and durably request its database provisioning.")
            .Produces<ApiResponse<OrganizationDto>>(201).ProducesProblem(400).ProducesProblem(409).ProducesProblem(503);
        platform.MapPost("/organizations/{publicId:guid}/retry", async (Guid publicId, HttpContext http, IOrganizationManagement organizations, CancellationToken ct) =>
            Results.Ok(new { data = await organizations.RetryAsync(publicId, Subject(http.User), ct) }))
            .WithSummary("Request a bounded retry for a failed organization.")
            .Produces<ApiResponse<OrganizationDto>>().ProducesProblem(404).ProducesProblem(409).ProducesProblem(503);
        platform.MapPost("/organizations/{publicId:guid}/administrator-invitation", async (Guid publicId, HttpContext http, IOrganizationManagement organizations, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { data = await organizations.IssueInvitationAsync(publicId, Subject(http.User), ct) });
        }).WithSummary("Issue a single-use administrator invitation for one-time manual handoff.")
            .Produces<ApiResponse<AdministratorInvitationDto>>().ProducesProblem(404).ProducesProblem(409).ProducesProblem(503);

        var activation = endpoints.MapGroup("/api/v1/activation").WithTags("Organization activation")
            .AllowAnonymous().RequireRateLimiting("authentication").AddEndpointFilter<OrganizationExceptionFilter>()
            .AddEndpointFilter(async (context, next) =>
            {
                try { await context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context.HttpContext); }
                catch (AntiforgeryValidationException)
                {
                    return Results.Problem(statusCode: 400, title: "Invalid request verification.", extensions: new Dictionary<string, object?> { ["code"] = "csrf-validation-failed" });
                }
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                return await next(context);
            });
        activation.MapPost("/inspect", async (InspectActivationRequest request, IOrganizationManagement organizations, CancellationToken ct) =>
        {
            var result = await organizations.InspectActivationAsync(request.Token, ct);
            return result is null ? InvalidInvitation() : Results.Ok(new { data = result });
        }).WithSummary("Inspect a valid, unused administrator invitation.")
            .Produces<ApiResponse<ActivationInspectionDto>>().ProducesProblem(400).ProducesProblem(503);
        activation.MapPost("/complete", async (CompleteActivationRequest request, IOrganizationManagement organizations, CancellationToken ct) =>
        {
            var result = await organizations.CompleteActivationAsync(request.Token, request.Password, ct);
            return result is null ? InvalidInvitation() : Results.Ok(new { data = result });
        }).WithSummary("Activate the initial administrator after persisting its tenant-owned grant.")
            .Produces<ApiResponse<ActivationCompletionDto>>().ProducesProblem(400).ProducesProblem(503);

        endpoints.MapGet("/api/v1/tenant/setup", async (HttpContext http, ITenantSetup setup, CancellationToken ct) =>
        {
            var result = await setup.GetAsync(Subject(http.User), ct);
            return result is null ? Results.Forbid() : Results.Ok(new { data = result });
        }).WithTags("Tenant setup").RequireAuthorization("OrganizationAdministrator").AddEndpointFilter<OrganizationExceptionFilter>()
            .WithSummary("Read the authenticated administrator's verified tenant setup.")
            .Produces<ApiResponse<TenantSetupDto>>().Produces(403).ProducesProblem(503);
        return endpoints;
    }

    private static Guid Subject(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier), out var subject)
            ? subject : throw new OrganizationOperationException("invalid-subject", "The authenticated account is invalid.", 401);

    private static IResult InvalidInvitation() => Results.Problem(statusCode: 400, title: "This invitation is invalid, expired, or already used.",
        extensions: new Dictionary<string, object?> { ["code"] = "invalid-invitation" });

    public sealed record InspectActivationRequest(string? Token);
    public sealed record CompleteActivationRequest(string? Token, string? Password);
    public sealed record ApiResponse<T>(T Data);
}

public sealed class OrganizationExceptionFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try { return await next(context); }
        catch (OrganizationOperationException exception)
        {
            return Results.Problem(statusCode: exception.StatusCode, title: exception.Message,
                extensions: new Dictionary<string, object?> { ["code"] = exception.Code });
        }
        catch (Exception exception) when (exception is SqlException or DbUpdateException or InvalidOperationException)
        {
            return Results.Problem(statusCode: 503, title: "The organization operation is temporarily unavailable.",
                extensions: new Dictionary<string, object?> { ["code"] = "organization-operation-unavailable" });
        }
    }
}

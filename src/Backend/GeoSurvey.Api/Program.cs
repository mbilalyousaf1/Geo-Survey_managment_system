using GeoSurvey.Api.Configuration;
using GeoSurvey.Api.Authentication;
using GeoSurvey.Api.Organizations;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((services, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(new CompactJsonFormatter()));

builder.Services.AddHealthChecks();
builder.Services.AddGlobalControlPlanePersistence(builder.Configuration);
builder.Services.AddGeoSurveyDataProtection(builder.Configuration, builder.Environment);
builder.Services.AddGeoSurveyAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddPhaseOneOrganizations(builder.Configuration);

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseMiddleware<SafeProblemMiddleware>();
app.UseRouting();
app.UseGeoSurveyAuthentication();
app.MapHealthChecks("/health/live");
app.MapGeoSurveyAuthentication();
app.MapPhaseOneOrganizations();
app.MapOpenApi();

app.Run();

// Exposes the generated entry point to WebApplicationFactory integration tests.
public partial class Program { }

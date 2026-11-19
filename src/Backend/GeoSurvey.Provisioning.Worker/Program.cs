using GeoSurvey.ControlPlane.Infrastructure;
using GeoSurvey.ControlPlane.Infrastructure.Tenancy;
using GeoSurvey.Identity.Infrastructure.Persistence;
using GeoSurvey.Provisioning.Worker;
using GeoSurvey.Tenancy.Application;
using GeoSurvey.Tenancy.Infrastructure;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<IdentityOptions>(IdentityPersistenceOptions.Configure);
builder.Services.AddOptions<LocalDemoTenantOptions>().Bind(builder.Configuration.GetSection(LocalDemoTenantOptions.SectionName))
    .Validate(options => options.IsValid(requireManagement: true), "Configure the worker's trusted tenant management/runtime profile.").ValidateOnStart();
var globalConnection = builder.Configuration.GetConnectionString("GlobalControlPlane");
var jobsConnection = builder.Configuration.GetConnectionString("ProvisioningJobs");
if (string.IsNullOrWhiteSpace(globalConnection) || string.IsNullOrWhiteSpace(jobsConnection))
    throw new InvalidOperationException("Configure worker GlobalControlPlane and ProvisioningJobs connections through external secrets.");
builder.Services.AddDbContext<GlobalControlPlaneDbContext>((provider, options) => options.UseGeoSurveyControlPlaneSqlServer(globalConnection, provider));
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddScoped<ITenantDatabaseOperations, SqlTenantDatabaseOperations>();
builder.Services.AddScoped<ITenantProvisioning, TenantProvisioningService>();
builder.Services.AddScoped<IProvisioningOutbox, ProvisioningOutboxDispatcher>();
builder.Services.AddSingleton<IProvisioningJobQueue, HangfireProvisioningQueue>();
builder.Services.AddHangfire(configuration => configuration.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer().UseRecommendedSerializerSettings()
    .UseSqlServerStorage(jobsConnection, new SqlServerStorageOptions
    {
        PrepareSchemaIfNecessary = true,
        QueuePollInterval = TimeSpan.FromSeconds(2),
        SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
        UseRecommendedIsolationLevel = true
    }));
builder.Services.AddHangfireServer(options =>
{
    options.Queues = ["tenant-provisioning"];
    options.WorkerCount = 2;
});
builder.Services.AddHostedService<OutboxBackgroundService>();
await builder.Build().RunAsync();

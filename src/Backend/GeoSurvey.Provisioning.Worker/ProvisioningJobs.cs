using GeoSurvey.Tenancy.Application;
using Hangfire;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GeoSurvey.Provisioning.Worker;

public sealed class HangfireProvisioningQueue(IBackgroundJobClient jobs) : IProvisioningJobQueue
{
    public string Enqueue(Guid organizationPublicId) => jobs.Enqueue<TenantProvisioningJob>(job => job.RunAsync(organizationPublicId, CancellationToken.None));
}

public sealed class TenantProvisioningJob(ITenantProvisioning provisioning)
{
    [Queue("tenant-provisioning")]
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [10, 30, 90], OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task RunAsync(Guid organizationPublicId, CancellationToken cancellationToken)
    {
        try { await provisioning.RunAsync(organizationPublicId, cancellationToken); }
        catch (Exception exception) when (exception is SqlException or DbUpdateException or InvalidOperationException)
        {
            // Include failures before the registry is opened: Hangfire persists exception details in its storage/logs.
            throw new InvalidOperationException("Tenant provisioning could not finish. The durable job will retry within its configured limit.");
        }
    }
}

public sealed class OutboxBackgroundService(IServiceScopeFactory scopes, IProvisioningJobQueue queue, ILogger<OutboxBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var count = await scope.ServiceProvider.GetRequiredService<IProvisioningOutbox>().DispatchAsync(queue, stoppingToken);
                if (count > 0) logger.LogInformation("Published {MessageCount} tenant provisioning requests.", count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception)
            {
                // Provider exceptions may contain server/database identifiers; log only a fixed safe operational event.
                logger.LogWarning("Provisioning outbox dispatch failed; pending requests remain durable and will be retried.");
            }
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
    }
}

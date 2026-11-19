using GeoSurvey.Tenancy.Application;
using Microsoft.EntityFrameworkCore;

namespace GeoSurvey.ControlPlane.Infrastructure.Tenancy;

/// <summary>At-least-once publication. A crash between enqueue and global commit can duplicate only an idempotent public-ID job.</summary>
public sealed class ProvisioningOutboxDispatcher(DbContextOptions<GlobalControlPlaneDbContext> options, TimeProvider clock) : IProvisioningOutbox
{
    public async Task<int> DispatchAsync(IProvisioningJobQueue queue, CancellationToken cancellationToken = default)
    {
        await using var context = PrivateControlPlane.Open(options);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var messages = await context.Set<ProvisioningOutboxMessage>()
            .FromSqlRaw("SELECT TOP(20) * FROM [tenancy].[ProvisioningOutbox] WITH (UPDLOCK, READPAST, ROWLOCK) WHERE [DispatchedAtUtc] IS NULL ORDER BY [Id]")
            .ToListAsync(cancellationToken);
        foreach (var message in messages)
        {
            message.JobId = queue.Enqueue(message.OrganizationPublicId);
            message.DispatchedAtUtc = clock.GetUtcNow();
        }
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return messages.Count;
    }
}

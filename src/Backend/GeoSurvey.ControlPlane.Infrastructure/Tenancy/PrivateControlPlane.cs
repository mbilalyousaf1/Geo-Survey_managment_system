using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeoSurvey.ControlPlane.Infrastructure.Tenancy;

internal static class PrivateControlPlane
{
    internal static GlobalControlPlaneDbContext Open(DbContextOptions<GlobalControlPlaneDbContext> options) => new(
        new DbContextOptionsBuilder<GlobalControlPlaneDbContext>(options)
            .UseLoggerFactory(NullLoggerFactory.Instance).LogTo(_ => { }, LogLevel.None)
            .EnableSensitiveDataLogging(false).Options);

    internal static async Task<RegistryLease> LockAsync(GlobalControlPlaneDbContext context, Guid organizationPublicId, CancellationToken ct)
    {
        await context.Database.OpenConnectionAsync(ct);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource=@resource, @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=30000; SELECT @result;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@resource";
        parameter.Value = "GeoSurveyRegistry:" + organizationPublicId.ToString("N");
        command.Parameters.Add(parameter);
        if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) < 0)
            throw new InvalidOperationException("The organization operation is busy. Retry shortly.");
        return new RegistryLease(context, (string)parameter.Value);
    }

    internal sealed class RegistryLease(GlobalControlPlaneDbContext context, string resource) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await using var release = context.Database.GetDbConnection().CreateCommand();
            release.CommandText = "EXEC sys.sp_releaseapplock @Resource=@resource, @LockOwner='Session';";
            var parameter = release.CreateParameter();
            parameter.ParameterName = "@resource";
            parameter.Value = resource;
            release.Parameters.Add(parameter);
            await release.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}

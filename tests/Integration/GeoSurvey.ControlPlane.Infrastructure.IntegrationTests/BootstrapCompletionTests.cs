using GeoSurvey.ControlPlane.Infrastructure.Bootstrap;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GeoSurvey.ControlPlane.Infrastructure.IntegrationTests;

public sealed class BootstrapCompletionTests(MigrationDatabase database) : IClassFixture<MigrationDatabase>
{
    [Fact]
    public async Task Completion_RoundTripsUtcAndRejectsSecondRecord()
    {
        await using var context = database.CreateContext();
        Assert.Empty(await context.BootstrapCompletions.ToListAsync());
        var before = DateTimeOffset.UtcNow;
        var completion = BootstrapCompletion.Create();
        Assert.Equal(1, completion.Id);
        Assert.Equal(TimeSpan.Zero, completion.CompletedAtUtc.Offset);
        Assert.InRange(completion.CompletedAtUtc, before, DateTimeOffset.UtcNow);
        context.Add(completion);
        await context.SaveChangesAsync();
        await using var reader = database.CreateContext();
        var loaded = await reader.BootstrapCompletions.SingleAsync();
        Assert.Equal(1, loaded.Id);
        Assert.Equal(completion.CompletedAtUtc.Ticks, loaded.CompletedAtUtc.Ticks);
        Assert.Equal(TimeSpan.Zero, loaded.CompletedAtUtc.Offset);
        reader.ChangeTracker.Clear();
        // A fresh tracking set ensures SQL Server, rather than EF's identity map, rejects the duplicate.
        reader.Add(BootstrapCompletion.Create());
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => reader.SaveChangesAsync());
        Assert.Contains(Assert.IsType<SqlException>(exception.InnerException).Number, new[] { 2601, 2627 });
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(2)]
    public async Task InvalidSingletonKey_IsRejected(int id)
    {
        await using var context = database.CreateContext();
        var exception = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [controlplane].[BootstrapCompletion] ([Id], [CompletedAtUtc]) VALUES ({id}, {DateTimeOffset.UtcNow})"));
        Assert.Equal(547, exception.Number);
    }

    [Fact]
    public void Marker_HasNoAccountRelationshipOrGeneratedTimestamp()
    {
        using var context = database.CreateContext();
        var entity = context.Model.FindEntityType(typeof(BootstrapCompletion))!;
        Assert.Empty(entity.GetForeignKeys());
        Assert.Empty(entity.GetNavigations());
        var timestamp = entity.FindProperty(nameof(BootstrapCompletion.CompletedAtUtc))!;
        Assert.False(timestamp.IsNullable);
        Assert.Equal("datetimeoffset(7)", timestamp.GetColumnType());
        Assert.Equal(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never, timestamp.ValueGenerated);
    }
}

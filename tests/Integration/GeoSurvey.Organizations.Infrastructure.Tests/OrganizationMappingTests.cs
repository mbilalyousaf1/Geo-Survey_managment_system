using GeoSurvey.Organizations.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GeoSurvey.Organizations.Infrastructure.Tests;

public sealed class OrganizationMappingTests(OrganizationDatabase database) : IClassFixture<OrganizationDatabase>
{
    private static Organization NewOrganization(string? code = null) =>
        Organization.Create(OrganizationCode.Create(code ?? "org-" + Guid.NewGuid().ToString("N")), "Survey Team");

    [Fact]
    public async Task FreshContext_PreservesEveryPersistedValueAndStoresCanonicalCode()
    {
        var original = NewOrganization(" MIXED-" + Guid.NewGuid().ToString("N") + " ");
        await using (var writer = database.CreateContext())
        {
            writer.Add(original);
            await writer.SaveChangesAsync();
        }

        await using var reader = database.CreateContext();
        var loaded = await reader.Organizations.SingleAsync(organization => organization.PublicId == original.PublicId);
        Assert.NotSame(original, loaded);
        Assert.True(loaded.Id > 0);
        Assert.Equal(original.Id, loaded.Id);
        Assert.Equal(original.PublicId, loaded.PublicId);
        Assert.Equal(original.Code, loaded.Code);
        Assert.Equal(original.DisplayName, loaded.DisplayName);
        Assert.Equal(original.CreatedAtUtc.Ticks, loaded.CreatedAtUtc.Ticks);
        Assert.Equal(TimeSpan.Zero, loaded.CreatedAtUtc.Offset);

        var rawCode = await reader.Database.SqlQuery<string>(
            $"SELECT [Code] AS [Value] FROM [organization].[Organizations] WHERE [Id] = {original.Id}").SingleAsync();
        Assert.Equal(original.Code.Value, rawCode);
    }

    [Fact]
    public async Task CodeEquality_TranslatesAndEquivalentValueDoesNotCauseUpdate()
    {
        var original = NewOrganization();
        await using var context = database.CreateContext();
        context.Add(original);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var equalCode = OrganizationCode.Create(original.Code.Value.ToUpperInvariant());
        var loaded = await context.Organizations.SingleAsync(organization => organization.Code == equalCode);
        context.Entry(loaded).Property(organization => organization.Code).CurrentValue = equalCode;
        context.ChangeTracker.DetectChanges();

        Assert.Equal(EntityState.Unchanged, context.Entry(loaded).State);
        Assert.Equal(0, await context.SaveChangesAsync());
    }

    [Fact]
    public async Task DuplicateDisplayNames_AreAllowed()
    {
        await using var context = database.CreateContext();
        context.AddRange(NewOrganization(), NewOrganization());
        Assert.Equal(2, await context.SaveChangesAsync());
    }

    [Fact]
    public async Task ConcurrentDuplicateCodes_ExactlyOneInsertSucceeds()
    {
        var code = "race-" + Guid.NewGuid().ToString("N");
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        first.Add(NewOrganization(code));
        second.Add(NewOrganization(code));

        static async Task<bool> SaveAsync(OrganizationTestContext context)
        {
            try
            {
                await context.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
            {
                return false;
            }
        }

        var results = await Task.WhenAll(SaveAsync(first), SaveAsync(second));
        Assert.Single(results, result => result);
        Assert.Single(results, result => !result);
    }

    [Fact]
    public async Task DuplicatePublicId_IsRejectedByDatabase()
    {
        var original = NewOrganization();
        await using var context = database.CreateContext();
        context.Add(original);
        await context.SaveChangesAsync();
        var otherCode = NewOrganization().Code.Value;

        var exception = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [organization].[Organizations] ([PublicId], [Code], [DisplayName], [CreatedAtUtc]) VALUES ({original.PublicId}, {otherCode}, {original.DisplayName}, {original.CreatedAtUtc})"));
        Assert.Contains(exception.Number, new[] { 2601, 2627 });
    }

    [Fact]
    public async Task InvalidStoredCode_FailsMaterialization()
    {
        var publicId = Guid.NewGuid();
        await using var context = database.CreateContext();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [organization].[Organizations] ([PublicId], [Code], [DisplayName], [CreatedAtUtc]) VALUES ({publicId}, {"invalid!"}, {"Survey Team"}, {DateTimeOffset.UtcNow})");

        await Assert.ThrowsAsync<ArgumentException>(() => context.Organizations.SingleAsync(organization => organization.PublicId == publicId));
    }

    [Fact]
    public void Model_DeclaresApprovedSqlServerStorageContract()
    {
        using var context = database.CreateContext();
        // The design-time model retains collation and other relational annotations.
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(Organization))!;
        Assert.Equal("organization", entity.GetSchema());
        Assert.Equal("Organizations", entity.GetTableName());
        Assert.Equal(ValueGenerated.OnAdd, entity.FindProperty(nameof(Organization.Id))!.ValueGenerated);
        Assert.Equal("uniqueidentifier", entity.FindProperty(nameof(Organization.PublicId))!.GetColumnType());
        Assert.Equal("nvarchar(50)", entity.FindProperty(nameof(Organization.Code))!.GetColumnType());
        Assert.Equal("Latin1_General_100_BIN2", entity.FindProperty(nameof(Organization.Code))!.GetCollation());
        Assert.Equal("nvarchar(200)", entity.FindProperty(nameof(Organization.DisplayName))!.GetColumnType());
        Assert.Equal("datetimeoffset(7)", entity.FindProperty(nameof(Organization.CreatedAtUtc))!.GetColumnType());
        foreach (var property in entity.GetProperties())
            Assert.False(property.IsNullable);
        foreach (var name in new[] { nameof(Organization.PublicId), nameof(Organization.Code) })
            Assert.Contains(entity.GetIndexes(), index => index.IsUnique && index.Properties.Single().Name == name);
        foreach (var name in new[] { nameof(Organization.PublicId), nameof(Organization.CreatedAtUtc) })
        {
            var property = entity.FindProperty(name)!;
            Assert.Equal(ValueGenerated.Never, property.ValueGenerated);
            Assert.Null(property.GetDefaultValueSql());
        }
    }
}

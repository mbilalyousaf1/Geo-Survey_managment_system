using System.Security.Cryptography;
using System.Text;
using GeoSurvey.ControlPlane.Infrastructure.Tenancy;
using GeoSurvey.Identity.Infrastructure;
using GeoSurvey.Organizations.Application;
using GeoSurvey.Tenancy.Application;
using GeoSurvey.Tenancy.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GeoSurvey.Tenancy.IntegrationTests;

public sealed class ProvisioningAndActivationTests
{
    private const string Password = "synthetic tenant administrator passphrase";

    [Fact]
    public async Task CreateAndProvision_AreDurableAndIdempotent_AccountHasNoPasswordUntilActivation()
    {
        await using var database = await TenantTestDatabase.CreateAsync();
        var organization = await database.CreateOrganizationAsync("alpha");
        Assert.Equal("Pending", organization.State);
        Assert.Null((await database.AdministratorAsync(organization.PublicId)).PasswordHash);
        await using (var global = database.CreateContext())
        {
            var outbox = await global.Set<ProvisioningOutboxMessage>().SingleAsync();
            Assert.Equal(organization.PublicId, outbox.OrganizationPublicId);
            Assert.Null(outbox.DispatchedAtUtc);
            Assert.Single(await global.Set<TenantLifecycleAudit>().ToListAsync());
        }
        await database.Provisioning.RunAsync(organization.PublicId);
        await database.Provisioning.RunAsync(organization.PublicId);
        Assert.Equal("AwaitingAdministrator", (await database.Organizations.GetAsync(organization.PublicId))!.State);
        var reference = await database.ReferenceAsync(organization.PublicId);
        await using var tenant = database.TenantContext(reference);
        var marker = await tenant.Identity.SingleAsync();
        Assert.Equal(organization.PublicId, marker.OrganizationPublicId);
        Assert.Equal(1, marker.SchemaVersion);
        Assert.Empty(await tenant.AdministratorGrants.ToListAsync());
        await using (var management = database.TenantContext(reference, management: true))
            Assert.Single(await management.Database.GetAppliedMigrationsAsync());
        await using var current = database.CreateContext();
        Assert.Equal(1, (await current.Set<TenantRegistryEntry>().SingleAsync()).ProvisioningAttempts);
        Assert.Null((await database.AdministratorAsync(organization.PublicId)).PasswordHash);
    }

    [Fact]
    public async Task Invitation_IsHashedReplacedSingleUse_ActivationPersistsGrantBeforeUsablePassword()
    {
        await using var database = await TenantTestDatabase.CreateAsync();
        var organization = await database.CreateOrganizationAsync("alpha");
        await Assert.ThrowsAsync<OrganizationOperationException>(() => database.Organizations.IssueInvitationAsync(organization.PublicId, database.ActorPublicId));
        await database.Provisioning.RunAsync(organization.PublicId);
        var old = await database.Organizations.IssueInvitationAsync(organization.PublicId, database.ActorPublicId);
        var current = await database.Organizations.IssueInvitationAsync(organization.PublicId, database.ActorPublicId);
        Assert.NotEqual(old.Token, current.Token);
        Assert.Null(await database.Organizations.InspectActivationAsync(old.Token));
        Assert.NotNull(await database.Organizations.InspectActivationAsync(current.Token));
        await using (var global = database.CreateContext())
        {
            var stored = await global.Set<AdministratorInvitation>().SingleAsync(row => row.RevokedAtUtc == null);
            Assert.Equal(SHA256.HashData(Encoding.ASCII.GetBytes(current.Token)), stored.TokenHash);
            Assert.Equal(TimeSpan.FromHours(24), stored.ExpiresAtUtc - stored.CreatedAtUtc);
        }
        Assert.NotNull(await database.Organizations.CompleteActivationAsync(current.Token, Password));
        Assert.Null(await database.Organizations.CompleteActivationAsync(current.Token, "different synthetic password"));
        Assert.Null(await database.Organizations.InspectActivationAsync(current.Token));
        await Assert.ThrowsAsync<OrganizationOperationException>(() => database.Organizations.IssueInvitationAsync(organization.PublicId, database.ActorPublicId));
        var account = await database.AdministratorAsync(organization.PublicId);
        var options = new PasswordHasherOptions();
        IdentityPasswordHashingOptions.Configure(options);
        Assert.Equal(PasswordVerificationResult.Success, new PasswordHasher<IdentityAccount>(Options.Create(options)).VerifyHashedPassword(account, account.PasswordHash!, Password));
        Assert.True(await database.Databases.HasAdministratorGrantAsync(await database.ReferenceAsync(organization.PublicId), account.PublicId));
        Assert.Equal("Ready", (await database.Organizations.GetAsync(organization.PublicId))!.State);
        Assert.True(await database.Access.CanAccessAsync(account.PublicId, organization.PublicId));
    }

    [Fact]
    public async Task TwoSameEmailAdministrators_HaveDifferentDatabasesAndCredentials_SelectorsCannotCrossTenants()
    {
        await using var database = await TenantTestDatabase.CreateAsync();
        var first = await database.CreateOrganizationAsync("alpha");
        var second = await database.CreateOrganizationAsync("beta");
        foreach (var organization in new[] { first, second })
        {
            await database.Provisioning.RunAsync(organization.PublicId);
            var invitation = await database.Organizations.IssueInvitationAsync(organization.PublicId, database.ActorPublicId);
            await database.Organizations.CompleteActivationAsync(invitation.Token, Password + organization.Code);
        }
        var firstAccount = await database.AdministratorAsync(first.PublicId);
        var secondAccount = await database.AdministratorAsync(second.PublicId);
        Assert.Equal(firstAccount.Email, secondAccount.Email);
        Assert.NotEqual(firstAccount.PublicId, secondAccount.PublicId);
        Assert.NotEqual(firstAccount.PasswordHash, secondAccount.PasswordHash);
        var firstReference = await database.ReferenceAsync(first.PublicId);
        var secondReference = await database.ReferenceAsync(second.PublicId);
        Assert.NotEqual(firstReference.DatabaseName, secondReference.DatabaseName);
        Assert.True(await database.Access.CanAccessAsync(firstAccount.PublicId, first.PublicId));
        Assert.False(await database.Access.CanAccessAsync(firstAccount.PublicId, second.PublicId));
        Assert.False(await database.Access.CanAccessAsync(secondAccount.PublicId, first.PublicId));
        Assert.False(await database.Access.CanAccessAsync(database.ActorPublicId, first.PublicId));
        Assert.Equal(first.PublicId, (await database.Access.GetAsync(firstAccount.PublicId))!.OrganizationPublicId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Databases.VerifyHealthAsync(firstReference with { DatabaseName = secondReference.DatabaseName }));

        await using (var management = database.TenantContext(firstReference, management: true))
            await management.AdministratorGrants.ExecuteDeleteAsync();
        Assert.False(await database.Access.CanAccessAsync(firstAccount.PublicId, first.PublicId));
        Assert.Null(await database.Access.GetAsync(firstAccount.PublicId));
        Assert.True(await database.Access.CanAccessAsync(secondAccount.PublicId, second.PublicId));
    }

    [Fact]
    public async Task MarkerMismatch_SuspensionAndRuntimePermissions_FailClosed()
    {
        await using var database = await TenantTestDatabase.CreateAsync();
        var organization = await database.CreateOrganizationAsync("alpha");
        await database.Provisioning.RunAsync(organization.PublicId);
        var invitation = await database.Organizations.IssueInvitationAsync(organization.PublicId, database.ActorPublicId);
        await database.Organizations.CompleteActivationAsync(invitation.Token, Password);
        var account = await database.AdministratorAsync(organization.PublicId);
        var reference = await database.ReferenceAsync(organization.PublicId);
        await using (var runtimeMaster = new SqlConnection(database.Profile.RuntimeConnectionString))
        {
            await runtimeMaster.OpenAsync();
            var probe = database.OwnedPermissionProbe();
            await using var create = new SqlCommand($"CREATE DATABASE [{probe.DatabaseName}]", runtimeMaster);
            await Assert.ThrowsAsync<SqlException>(() => create.ExecuteNonQueryAsync());
        }
        await using (var runtime = database.TenantContext(reference))
        {
            await Assert.ThrowsAsync<SqlException>(() => runtime.Database.ExecuteSqlRawAsync("UPDATE [tenant].[OrganizationIdentity] SET [SchemaVersion]=2"));
            await Assert.ThrowsAsync<SqlException>(() => runtime.Database.ExecuteSqlRawAsync("CREATE TABLE [tenant].[Unauthorized] ([Id] int)"));
        }
        await using (var global = database.CreateContext())
            await global.Set<TenantRegistryEntry>().ExecuteUpdateAsync(setters => setters.SetProperty(row => row.State, TenantProvisioningState.Suspended));
        Assert.False(await database.Access.CanAccessAsync(account.PublicId, organization.PublicId));
        await using (var global = database.CreateContext())
            await global.Set<TenantRegistryEntry>().ExecuteUpdateAsync(setters => setters.SetProperty(row => row.State, TenantProvisioningState.Ready));
        await using (var management = database.TenantContext(reference, management: true))
            await management.Database.ExecuteSqlInterpolatedAsync($"UPDATE [tenant].[OrganizationIdentity] SET [OrganizationPublicId]={Guid.NewGuid()}");
        Assert.False(await database.Access.CanAccessAsync(account.PublicId, organization.PublicId));
    }

    [Fact]
    public async Task ExpiredInvitationAndInvalidPassword_DoNotEnableAccountOrWriteTenantGrant()
    {
        await using var database = await TenantTestDatabase.CreateAsync();
        var organization = await database.CreateOrganizationAsync("alpha");
        await database.Provisioning.RunAsync(organization.PublicId);
        var invitation = await database.Organizations.IssueInvitationAsync(organization.PublicId, database.ActorPublicId);
        await using (var global = database.CreateContext())
        {
            var before = DateTimeOffset.UtcNow.AddHours(-25);
            var expired = DateTimeOffset.UtcNow.AddHours(-1);
            await global.Set<AdministratorInvitation>().ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.CreatedAtUtc, before).SetProperty(row => row.ExpiresAtUtc, expired));
        }
        Assert.Null(await database.Organizations.InspectActivationAsync(invitation.Token));
        Assert.Null(await database.Organizations.CompleteActivationAsync(invitation.Token, Password));
        var replacement = await database.Organizations.IssueInvitationAsync(organization.PublicId, database.ActorPublicId);
        var invalid = await Assert.ThrowsAsync<OrganizationOperationException>(() => database.Organizations.CompleteActivationAsync(replacement.Token, "short"));
        Assert.Equal("invalid-password", invalid.Code);
        var account = await database.AdministratorAsync(organization.PublicId);
        Assert.Null(account.PasswordHash);
        Assert.False(await database.Databases.HasAdministratorGrantAsync(await database.ReferenceAsync(organization.PublicId), account.PublicId));
        Assert.NotNull(await database.Organizations.InspectActivationAsync(replacement.Token));
        Assert.Equal("AwaitingAdministrator", (await database.Organizations.GetAsync(organization.PublicId))!.State);
    }

    [Fact]
    public async Task GlobalActivationFailure_LeavesPasswordlessAccountAndResumableGrant()
    {
        await using var database = await TenantTestDatabase.CreateAsync();
        var organization = await database.CreateOrganizationAsync("alpha");
        await database.Provisioning.RunAsync(organization.PublicId);
        var invitation = await database.Organizations.IssueInvitationAsync(organization.PublicId, database.ActorPublicId);
        await using (var global = database.CreateContext())
            await global.Database.ExecuteSqlRawAsync("ALTER TABLE [identity].[Accounts] ADD CONSTRAINT [CK_TestRejectTenantPassword] CHECK ([AccountScope] <> 2 OR [PasswordHash] IS NULL)");
        await Assert.ThrowsAsync<DbUpdateException>(() => database.Organizations.CompleteActivationAsync(invitation.Token, Password));
        var account = await database.AdministratorAsync(organization.PublicId);
        Assert.Null(account.PasswordHash);
        Assert.True(await database.Databases.HasAdministratorGrantAsync(await database.ReferenceAsync(organization.PublicId), account.PublicId));
        Assert.False(await database.Access.CanAccessAsync(account.PublicId, organization.PublicId));
        Assert.NotNull(await database.Organizations.InspectActivationAsync(invitation.Token));
        await using (var global = database.CreateContext())
            await global.Database.ExecuteSqlRawAsync("ALTER TABLE [identity].[Accounts] DROP CONSTRAINT [CK_TestRejectTenantPassword]");
        Assert.NotNull(await database.Organizations.CompleteActivationAsync(invitation.Token, Password));
        Assert.True(await database.Access.CanAccessAsync(account.PublicId, organization.PublicId));
    }

    [Fact]
    public async Task OutboxDispatch_RetainsMessagesOnQueueFailure_AndDuplicateJobsDoNotReprovision()
    {
        await using var database = await TenantTestDatabase.CreateAsync();
        var organization = await database.CreateOrganizationAsync("alpha");
        var dispatcher = new ProvisioningOutboxDispatcher(database.Options, TimeProvider.System);
        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.DispatchAsync(new BrokenQueue()));
        await using (var global = database.CreateContext()) Assert.Null((await global.Set<ProvisioningOutboxMessage>().SingleAsync()).DispatchedAtUtc);
        var queue = new RecordingQueue();
        Assert.Equal(1, await dispatcher.DispatchAsync(queue));
        Assert.Equal(0, await dispatcher.DispatchAsync(queue));
        Assert.Equal(organization.PublicId, Assert.Single(queue.Organizations));
        await Task.WhenAll(database.Provisioning.RunAsync(organization.PublicId), database.Provisioning.RunAsync(organization.PublicId));
        await using var current = database.CreateContext();
        Assert.Equal(1, (await current.Set<TenantRegistryEntry>().SingleAsync()).ProvisioningAttempts);
    }

    [Fact]
    public async Task WorkerRestartAfterTenantCreation_ResumesTheSameDatabase()
    {
        await using var database = await TenantTestDatabase.CreateAsync();
        var organization = await database.CreateOrganizationAsync("alpha");
        var reference = await database.ReferenceAsync(organization.PublicId);
        // The tenant commit completed, then the process stopped before writing the global completion state.
        await database.Databases.ProvisionAsync(reference);
        DateTimeOffset created;
        await using (var existing = database.TenantContext(reference)) created = (await existing.Identity.SingleAsync()).CreatedAtUtc;
        Assert.Equal("Pending", (await database.Organizations.GetAsync(organization.PublicId))!.State);
        await database.Provisioning.RunAsync(organization.PublicId);
        await using var resumed = database.TenantContext(reference);
        Assert.Equal(created, (await resumed.Identity.SingleAsync()).CreatedAtUtc);
        await using (var management = database.TenantContext(reference, management: true))
            Assert.Single(await management.Database.GetAppliedMigrationsAsync());
        Assert.Equal("AwaitingAdministrator", (await database.Organizations.GetAsync(organization.PublicId))!.State);
    }

    [Fact]
    public async Task ExistingUnownedDatabase_IsPreserved_RetriesAreBounded_AndOperatorCanResume()
    {
        await using var database = await TenantTestDatabase.CreateAsync();
        var organization = await database.CreateOrganizationAsync("alpha");
        var reference = await database.ReferenceAsync(organization.PublicId);
        await using (var master = new SqlConnection(database.Profile.ManagementConnectionString))
        {
            await master.OpenAsync();
            await using var create = new SqlCommand($"CREATE DATABASE [{reference.DatabaseName}]", master);
            await create.ExecuteNonQueryAsync();
        }
        for (var attempt = 0; attempt < TenantProvisioningService.MaximumAttempts; attempt++)
        {
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => database.Provisioning.RunAsync(organization.PublicId));
            Assert.Null(failure.InnerException);
            Assert.DoesNotContain(reference.DatabaseName, failure.ToString());
        }
        await database.Provisioning.RunAsync(organization.PublicId); // Retry budget exhausted: no more mutation.
        Assert.Equal("Failed", (await database.Organizations.GetAsync(organization.PublicId))!.State);
        await using (var current = database.CreateContext())
            Assert.Equal(TenantProvisioningService.MaximumAttempts, (await current.Set<TenantRegistryEntry>().SingleAsync()).ProvisioningAttempts);
        await using (var existing = new SqlConnection(database.Profile.ConnectionFor(reference, management: true)))
        {
            await existing.OpenAsync();
            await using var tables = new SqlCommand("SELECT COUNT(*) FROM sys.tables WHERE [is_ms_shipped]=0", existing);
            Assert.Equal(0, Convert.ToInt32(await tables.ExecuteScalarAsync()));
            // Controlled operator repair of this synthetic, explicitly owned test target; runtime cannot bind ownership.
            await using var bind = new SqlCommand("EXEC sys.sp_addextendedproperty @name=N'GeoSurvey.OrganizationPublicId', @value=@organization", existing);
            bind.Parameters.AddWithValue("@organization", organization.PublicId.ToString("D"));
            await bind.ExecuteNonQueryAsync();
        }
        Assert.Equal("Pending", (await database.Organizations.RetryAsync(organization.PublicId, database.ActorPublicId)).State);
        await database.Provisioning.RunAsync(organization.PublicId);
        Assert.Equal("AwaitingAdministrator", (await database.Organizations.GetAsync(organization.PublicId))!.State);
    }

    private sealed class BrokenQueue : IProvisioningJobQueue
    {
        public string Enqueue(Guid organizationPublicId) => throw new InvalidOperationException("synthetic queue failure");
    }
    private sealed class RecordingQueue : IProvisioningJobQueue
    {
        public List<Guid> Organizations { get; } = [];
        public string Enqueue(Guid organizationPublicId) { Organizations.Add(organizationPublicId); return "synthetic-job"; }
    }
}

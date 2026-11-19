using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoSurvey.ControlPlane.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantProvisioningAndActivation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "tenancy");

            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "AdministratorInvitations",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdministratorInvitations", x => x.Id);
                    table.CheckConstraint("CK_AdministratorInvitations_Expiry", "[ExpiresAtUtc] > [CreatedAtUtc]");
                    table.CheckConstraint("CK_AdministratorInvitations_Outcome", "[ConsumedAtUtc] IS NULL OR [RevokedAtUtc] IS NULL");
                    table.ForeignKey(
                        name: "FK_AdministratorInvitations_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "identity",
                        principalTable: "Accounts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AdministratorInvitations_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "organization",
                        principalTable: "Organizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ProvisioningOutbox",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    OrganizationPublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    DispatchedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    JobId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProvisioningOutbox", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProvisioningOutbox_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "organization",
                        principalTable: "Organizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TenantLifecycleAudit",
                schema: "audit",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationPublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorPublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EventType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantLifecycleAudit", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantRegistry",
                schema: "tenancy",
                columns: table => new
                {
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    RegionKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DatabaseName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    State = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    InitialAdministratorAccountId = table.Column<int>(type: "int", nullable: false),
                    ProvisioningAttempts = table.Column<int>(type: "int", nullable: false),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false),
                    FailureCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    DatabaseHealthyAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantRegistry", x => x.OrganizationId);
                    table.CheckConstraint("CK_TenantRegistry_Attempts", "[ProvisioningAttempts] >= 0");
                    table.CheckConstraint("CK_TenantRegistry_Ready", "[State] NOT IN (N'Ready',N'AwaitingAdministrator') OR ([SchemaVersion] = 1 AND [DatabaseHealthyAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_TenantRegistry_State", "[State] IN (N'Pending',N'Provisioning',N'AwaitingAdministrator',N'Ready',N'Failed',N'Suspended')");
                    table.ForeignKey(
                        name: "FK_TenantRegistry_Accounts_InitialAdministratorAccountId",
                        column: x => x.InitialAdministratorAccountId,
                        principalSchema: "identity",
                        principalTable: "Accounts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TenantRegistry_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "organization",
                        principalTable: "Organizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdministratorInvitations_AccountId",
                schema: "tenancy",
                table: "AdministratorInvitations",
                column: "AccountId",
                unique: true,
                filter: "[ConsumedAtUtc] IS NULL AND [RevokedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AdministratorInvitations_OrganizationId",
                schema: "tenancy",
                table: "AdministratorInvitations",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_AdministratorInvitations_PublicId",
                schema: "tenancy",
                table: "AdministratorInvitations",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdministratorInvitations_TokenHash",
                schema: "tenancy",
                table: "AdministratorInvitations",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProvisioningOutbox_DispatchedAtUtc",
                schema: "tenancy",
                table: "ProvisioningOutbox",
                column: "DispatchedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ProvisioningOutbox_OrganizationId",
                schema: "tenancy",
                table: "ProvisioningOutbox",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_ProvisioningOutbox_PublicId",
                schema: "tenancy",
                table: "ProvisioningOutbox",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantLifecycleAudit_OrganizationPublicId_OccurredAtUtc",
                schema: "audit",
                table: "TenantLifecycleAudit",
                columns: new[] { "OrganizationPublicId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TenantLifecycleAudit_PublicId",
                schema: "audit",
                table: "TenantLifecycleAudit",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantRegistry_DatabaseName",
                schema: "tenancy",
                table: "TenantRegistry",
                column: "DatabaseName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantRegistry_InitialAdministratorAccountId",
                schema: "tenancy",
                table: "TenantRegistry",
                column: "InitialAdministratorAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdministratorInvitations",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "ProvisioningOutbox",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "TenantLifecycleAudit",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "TenantRegistry",
                schema: "tenancy");
        }
    }
}

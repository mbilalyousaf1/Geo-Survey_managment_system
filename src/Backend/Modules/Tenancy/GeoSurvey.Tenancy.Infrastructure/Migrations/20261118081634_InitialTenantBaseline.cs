using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoSurvey.Tenancy.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialTenantBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "tenant");

            migrationBuilder.CreateTable(
                name: "AdministratorGrants",
                schema: "tenant",
                columns: table => new
                {
                    AccountPublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GrantedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdministratorGrants", x => x.AccountPublicId);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationIdentity",
                schema: "tenant",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    OrganizationPublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationIdentity", x => x.Id);
                    table.CheckConstraint("CK_OrganizationIdentity_SchemaVersion", "[SchemaVersion] >= 1");
                    table.CheckConstraint("CK_OrganizationIdentity_Singleton", "[Id] = 1");
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationIdentity_OrganizationPublicId",
                schema: "tenant",
                table: "OrganizationIdentity",
                column: "OrganizationPublicId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdministratorGrants",
                schema: "tenant");

            migrationBuilder.DropTable(
                name: "OrganizationIdentity",
                schema: "tenant");
        }
    }
}

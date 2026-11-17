using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoSurvey.ControlPlane.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBootstrapCompletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "controlplane");

            migrationBuilder.CreateTable(
                name: "BootstrapCompletion",
                schema: "controlplane",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BootstrapCompletion", x => x.Id);
                    table.CheckConstraint("CK_BootstrapCompletion_Singleton", "[Id] = 1");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BootstrapCompletion",
                schema: "controlplane");
        }
    }
}

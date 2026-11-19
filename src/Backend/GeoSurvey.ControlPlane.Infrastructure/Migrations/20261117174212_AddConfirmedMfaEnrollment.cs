using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoSurvey.ControlPlane.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConfirmedMfaEnrollment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MfaRecoveryCodes",
                schema: "identity",
                columns: table => new
                {
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    CodeHash = table.Column<string>(type: "char(64)", unicode: false, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfaRecoveryCodes", x => new { x.AccountId, x.CodeHash });
                    table.CheckConstraint("CK_MfaRecoveryCodes_ConsumedAt", "[ConsumedAtUtc] IS NULL OR [ConsumedAtUtc] >= [CreatedAtUtc]");
                    table.ForeignKey(
                        name: "FK_MfaRecoveryCodes_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "identity",
                        principalTable: "Accounts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TotpCredentials",
                schema: "identity",
                columns: table => new
                {
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    ProtectedSecret = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConfirmedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TotpCredentials", x => x.AccountId);
                    table.ForeignKey(
                        name: "FK_TotpCredentials_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "identity",
                        principalTable: "Accounts",
                        principalColumn: "Id");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MfaRecoveryCodes",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "TotpCredentials",
                schema: "identity");
        }
    }
}

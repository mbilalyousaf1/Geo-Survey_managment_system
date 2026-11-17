using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoSurvey.ControlPlane.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingTotpEnrollments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_MfaChallenges_Id_AccountId",
                schema: "identity",
                table: "MfaChallenges",
                columns: new[] { "Id", "AccountId" });

            migrationBuilder.CreateTable(
                name: "PendingTotpEnrollments",
                schema: "identity",
                columns: table => new
                {
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    ChallengeId = table.Column<int>(type: "int", nullable: false),
                    ProtectedSecret = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingTotpEnrollments", x => x.AccountId);
                    table.ForeignKey(
                        name: "FK_PendingTotpEnrollments_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "identity",
                        principalTable: "Accounts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PendingTotpEnrollments_MfaChallenges_ChallengeId_AccountId",
                        columns: x => new { x.ChallengeId, x.AccountId },
                        principalSchema: "identity",
                        principalTable: "MfaChallenges",
                        principalColumns: new[] { "Id", "AccountId" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_PendingTotpEnrollments_ChallengeId",
                schema: "identity",
                table: "PendingTotpEnrollments",
                column: "ChallengeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PendingTotpEnrollments_ChallengeId_AccountId",
                schema: "identity",
                table: "PendingTotpEnrollments",
                columns: new[] { "ChallengeId", "AccountId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PendingTotpEnrollments",
                schema: "identity");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_MfaChallenges_Id_AccountId",
                schema: "identity",
                table: "MfaChallenges");
        }
    }
}

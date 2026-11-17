using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoSurvey.ControlPlane.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMfaVerificationStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MfaVerificationStates",
                schema: "identity",
                columns: table => new
                {
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    FailedAttemptCount = table.Column<int>(type: "int", nullable: false),
                    FailureWindowStartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    BlockedUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    LastAcceptedStep = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfaVerificationStates", x => x.AccountId);
                    table.CheckConstraint("CK_MfaVerificationStates_Block", "[BlockedUntilUtc] IS NULL OR [FailedAttemptCount] = 5");
                    table.CheckConstraint("CK_MfaVerificationStates_Count", "[FailedAttemptCount] BETWEEN 0 AND 5");
                    table.CheckConstraint("CK_MfaVerificationStates_Step", "[LastAcceptedStep] IS NULL OR [LastAcceptedStep] >= 0");
                    table.CheckConstraint("CK_MfaVerificationStates_Window", "([FailedAttemptCount] = 0 AND [FailureWindowStartedAtUtc] IS NULL) OR ([FailedAttemptCount] > 0 AND [FailureWindowStartedAtUtc] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_MfaVerificationStates_Accounts_AccountId",
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
                name: "MfaVerificationStates",
                schema: "identity");
        }
    }
}

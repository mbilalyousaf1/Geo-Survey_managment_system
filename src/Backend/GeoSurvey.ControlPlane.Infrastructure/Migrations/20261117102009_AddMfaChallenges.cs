using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoSurvey.ControlPlane.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMfaChallenges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MfaChallenges",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    Purpose = table.Column<int>(type: "int", nullable: false),
                    TokenHash = table.Column<string>(type: "char(64)", unicode: false, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SecurityStamp = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfaChallenges", x => x.Id);
                    table.CheckConstraint("CK_MfaChallenges_Expiry", "[ExpiresAtUtc] > [CreatedAtUtc]");
                    table.CheckConstraint("CK_MfaChallenges_Purpose", "[Purpose] IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_MfaChallenges_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "identity",
                        principalTable: "Accounts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_MfaChallenges_AccountId_Purpose",
                schema: "identity",
                table: "MfaChallenges",
                columns: new[] { "AccountId", "Purpose" },
                unique: true,
                filter: "[ConsumedAtUtc] IS NULL AND [RevokedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MfaChallenges_TokenHash",
                schema: "identity",
                table: "MfaChallenges",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MfaChallenges",
                schema: "identity");
        }
    }
}

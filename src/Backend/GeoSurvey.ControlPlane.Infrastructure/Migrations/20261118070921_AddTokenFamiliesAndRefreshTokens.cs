using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoSurvey.ControlPlane.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTokenFamiliesAndRefreshTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TokenFamilies",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    SecurityStamp = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TokenFamilies", x => x.Id);
                    table.CheckConstraint("CK_TokenFamilies_Expiry", "[ExpiresAtUtc] > [CreatedAtUtc]");
                    table.CheckConstraint("CK_TokenFamilies_PublicId", "[PublicId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_TokenFamilies_RevokedAt", "[RevokedAtUtc] IS NULL OR [RevokedAtUtc] >= [CreatedAtUtc]");
                    table.CheckConstraint("CK_TokenFamilies_SecurityStamp", "LEN([SecurityStamp]) > 0");
                    table.CheckConstraint("CK_TokenFamilies_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [ExpiresAtUtc]) = 0 AND ([RevokedAtUtc] IS NULL OR DATEPART(TZOFFSET, [RevokedAtUtc]) = 0)");
                    table.ForeignKey(
                        name: "FK_TokenFamilies_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "identity",
                        principalTable: "Accounts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TokenFamilyId = table.Column<long>(type: "bigint", nullable: false),
                    TokenHash = table.Column<string>(type: "char(64)", unicode: false, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                    table.CheckConstraint("CK_RefreshTokens_ConsumedAt", "[ConsumedAtUtc] IS NULL OR [ConsumedAtUtc] >= [CreatedAtUtc]");
                    table.CheckConstraint("CK_RefreshTokens_Expiry", "[ExpiresAtUtc] > [CreatedAtUtc]");
                    table.CheckConstraint("CK_RefreshTokens_TokenHash", "LEN([TokenHash]) = 64 AND [TokenHash] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9A-F]%'");
                    table.CheckConstraint("CK_RefreshTokens_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [ExpiresAtUtc]) = 0 AND ([ConsumedAtUtc] IS NULL OR DATEPART(TZOFFSET, [ConsumedAtUtc]) = 0)");
                    table.ForeignKey(
                        name: "FK_RefreshTokens_TokenFamilies_TokenFamilyId",
                        column: x => x.TokenFamilyId,
                        principalSchema: "identity",
                        principalTable: "TokenFamilies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenFamilyId",
                schema: "identity",
                table: "RefreshTokens",
                column: "TokenFamilyId",
                unique: true,
                filter: "[ConsumedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenHash",
                schema: "identity",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TokenFamilies_AccountId",
                schema: "identity",
                table: "TokenFamilies",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_TokenFamilies_PublicId",
                schema: "identity",
                table: "TokenFamilies",
                column: "PublicId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RefreshTokens",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "TokenFamilies",
                schema: "identity");
        }
    }
}

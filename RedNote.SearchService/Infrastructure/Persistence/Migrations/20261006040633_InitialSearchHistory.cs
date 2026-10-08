using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RedNote.SearchService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSearchHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "search");

            migrationBuilder.CreateTable(
                name: "SearchHistory",
                schema: "search",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Keyword = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NormalizedKeyword = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LastSearchedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchHistory", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SearchHistory_UserId_LastSearchedAtUtc",
                schema: "search",
                table: "SearchHistory",
                columns: new[] { "UserId", "LastSearchedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SearchHistory_UserId_NormalizedKeyword",
                schema: "search",
                table: "SearchHistory",
                columns: new[] { "UserId", "NormalizedKeyword" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SearchHistory",
                schema: "search");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RedNote.ContentService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdminAuditIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AdminAudit_ActorUserId_CreatedAtUtc",
                table: "AdminAudit",
                columns: new[] { "ActorUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AdminAudit_CreatedAtUtc",
                table: "AdminAudit",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AdminAudit_TargetId_CreatedAtUtc",
                table: "AdminAudit",
                columns: new[] { "TargetId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AdminAudit_ActorUserId_CreatedAtUtc",
                table: "AdminAudit");

            migrationBuilder.DropIndex(
                name: "IX_AdminAudit_CreatedAtUtc",
                table: "AdminAudit");

            migrationBuilder.DropIndex(
                name: "IX_AdminAudit_TargetId_CreatedAtUtc",
                table: "AdminAudit");
        }
    }
}

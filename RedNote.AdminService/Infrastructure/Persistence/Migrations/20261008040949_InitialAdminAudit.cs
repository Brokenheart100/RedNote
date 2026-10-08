using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RedNote.AdminService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAdminAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdminAuditProjections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    TargetType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    TraceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminAuditProjections", x => new { x.Source, x.Id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdminAuditProjections_ActorUserId_CreatedAtUtc",
                table: "AdminAuditProjections",
                columns: new[] { "ActorUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AdminAuditProjections_CreatedAtUtc_Id",
                table: "AdminAuditProjections",
                columns: new[] { "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AdminAuditProjections_Source_CreatedAtUtc",
                table: "AdminAuditProjections",
                columns: new[] { "Source", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AdminAuditProjections_TargetId_CreatedAtUtc",
                table: "AdminAuditProjections",
                columns: new[] { "TargetId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminAuditProjections");
        }
    }
}

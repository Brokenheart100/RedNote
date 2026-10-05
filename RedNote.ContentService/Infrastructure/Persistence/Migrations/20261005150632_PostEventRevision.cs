using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RedNote.ContentService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PostEventRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Revision",
                table: "Posts",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Revision",
                table: "Posts");
        }
    }
}

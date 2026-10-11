using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Charac.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class FileUploadGrantPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CanUpload",
                schema: "access",
                table: "pre_registered_grants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CanUpload",
                schema: "access",
                table: "grants",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CanUpload",
                schema: "access",
                table: "pre_registered_grants");

            migrationBuilder.DropColumn(
                name: "CanUpload",
                schema: "access",
                table: "grants");
        }
    }
}

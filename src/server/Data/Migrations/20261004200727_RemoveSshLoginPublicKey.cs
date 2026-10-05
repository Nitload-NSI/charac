using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Charac.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSshLoginPublicKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PublicKey",
                schema: "access",
                table: "ssh_login_keys");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PublicKey",
                schema: "access",
                table: "ssh_login_keys",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: false,
                defaultValue: "");
        }
    }
}

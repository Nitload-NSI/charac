using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Charac.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class SshLoginKeysAndWorkspaceHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SshLoginKeyId",
                schema: "access",
                table: "grants",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ssh_login_keys",
                schema: "access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    PublicKey = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ssh_login_keys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workspace_records",
                schema: "access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Issuer = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Account = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Authentication = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    OpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndReason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspace_records", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_grants_SshLoginKeyId",
                schema: "access",
                table: "grants",
                column: "SshLoginKeyId");

            migrationBuilder.CreateIndex(
                name: "IX_ssh_login_keys_FileName",
                schema: "access",
                table: "ssh_login_keys",
                column: "FileName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ssh_login_keys_Name",
                schema: "access",
                table: "ssh_login_keys",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workspace_records_Issuer_Subject_OpenedAt",
                schema: "access",
                table: "workspace_records",
                columns: new[] { "Issuer", "Subject", "OpenedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_grants_ssh_login_keys_SshLoginKeyId",
                schema: "access",
                table: "grants",
                column: "SshLoginKeyId",
                principalSchema: "access",
                principalTable: "ssh_login_keys",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_grants_ssh_login_keys_SshLoginKeyId",
                schema: "access",
                table: "grants");

            migrationBuilder.DropTable(
                name: "ssh_login_keys",
                schema: "access");

            migrationBuilder.DropTable(
                name: "workspace_records",
                schema: "access");

            migrationBuilder.DropIndex(
                name: "IX_grants_SshLoginKeyId",
                schema: "access",
                table: "grants");

            migrationBuilder.DropColumn(
                name: "SshLoginKeyId",
                schema: "access",
                table: "grants");
        }
    }
}

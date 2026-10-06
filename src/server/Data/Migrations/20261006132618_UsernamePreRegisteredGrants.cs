using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Charac.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class UsernamePreRegisteredGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UserName",
                schema: "access",
                table: "identities",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "pre_registered_grants",
                schema: "access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Issuer = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Account = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SshLoginKeyId = table.Column<Guid>(type: "uuid", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pre_registered_grants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pre_registered_grants_ssh_login_keys_SshLoginKeyId",
                        column: x => x.SshLoginKeyId,
                        principalSchema: "access",
                        principalTable: "ssh_login_keys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pre_registered_grants_targets_TargetId",
                        column: x => x.TargetId,
                        principalSchema: "access",
                        principalTable: "targets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_identities_Issuer_UserName",
                schema: "access",
                table: "identities",
                columns: new[] { "Issuer", "UserName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pre_registered_grants_Issuer_UserName_TargetId",
                schema: "access",
                table: "pre_registered_grants",
                columns: new[] { "Issuer", "UserName", "TargetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pre_registered_grants_SshLoginKeyId",
                schema: "access",
                table: "pre_registered_grants",
                column: "SshLoginKeyId");

            migrationBuilder.CreateIndex(
                name: "IX_pre_registered_grants_TargetId",
                schema: "access",
                table: "pre_registered_grants",
                column: "TargetId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pre_registered_grants",
                schema: "access");

            migrationBuilder.DropIndex(
                name: "IX_identities_Issuer_UserName",
                schema: "access",
                table: "identities");

            migrationBuilder.DropColumn(
                name: "UserName",
                schema: "access",
                table: "identities");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Charac.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class EndpointAccountCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_pre_registered_grants_Issuer_UserName_TargetId",
                schema: "access",
                table: "pre_registered_grants");

            migrationBuilder.DropIndex(
                name: "IX_grants_IdentityId_TargetId",
                schema: "access",
                table: "grants");

            migrationBuilder.AddColumn<Guid>(
                name: "EndpointAccountId",
                schema: "access",
                table: "pre_registered_grants",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "EndpointAccountId",
                schema: "access",
                table: "grants",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "endpoint_accounts",
                schema: "access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Account = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SshLoginKeyId = table.Column<Guid>(type: "uuid", nullable: true),
                    CertificatePrincipal = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_endpoint_accounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_endpoint_accounts_ssh_login_keys_SshLoginKeyId",
                        column: x => x.SshLoginKeyId,
                        principalSchema: "access",
                        principalTable: "ssh_login_keys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_endpoint_accounts_targets_TargetId",
                        column: x => x.TargetId,
                        principalSchema: "access",
                        principalTable: "targets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql("""
                INSERT INTO access.endpoint_accounts
                    ("Id", "TargetId", "Account", "SshLoginKeyId", "CertificatePrincipal", "Enabled")
                SELECT DISTINCT ON ("TargetId", "Account")
                    "Id", "TargetId", "Account", "SshLoginKeyId", "CertificatePrincipal", TRUE
                FROM access.grants
                ORDER BY "TargetId", "Account", "Id";

                INSERT INTO access.endpoint_accounts
                    ("Id", "TargetId", "Account", "SshLoginKeyId", "CertificatePrincipal", "Enabled")
                SELECT DISTINCT ON (pending."TargetId", pending."Account")
                    pending."Id", pending."TargetId", pending."Account", pending."SshLoginKeyId", NULL, TRUE
                FROM access.pre_registered_grants AS pending
                WHERE NOT EXISTS
                (
                    SELECT 1
                    FROM access.endpoint_accounts AS endpoint_account
                    WHERE endpoint_account."TargetId" = pending."TargetId"
                      AND endpoint_account."Account" = pending."Account"
                )
                ORDER BY pending."TargetId", pending."Account", pending."Id";

                UPDATE access.grants AS grant_record
                SET "EndpointAccountId" = endpoint_account."Id"
                FROM access.endpoint_accounts AS endpoint_account
                WHERE endpoint_account."TargetId" = grant_record."TargetId"
                  AND endpoint_account."Account" = grant_record."Account";

                UPDATE access.pre_registered_grants AS pending
                SET "EndpointAccountId" = endpoint_account."Id"
                FROM access.endpoint_accounts AS endpoint_account
                WHERE endpoint_account."TargetId" = pending."TargetId"
                  AND endpoint_account."Account" = pending."Account";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_pre_registered_grants_EndpointAccountId",
                schema: "access",
                table: "pre_registered_grants",
                column: "EndpointAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_pre_registered_grants_Issuer_UserName_EndpointAccountId",
                schema: "access",
                table: "pre_registered_grants",
                columns: new[] { "Issuer", "UserName", "EndpointAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_grants_EndpointAccountId",
                schema: "access",
                table: "grants",
                column: "EndpointAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_grants_IdentityId_EndpointAccountId",
                schema: "access",
                table: "grants",
                columns: new[] { "IdentityId", "EndpointAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_endpoint_accounts_SshLoginKeyId",
                schema: "access",
                table: "endpoint_accounts",
                column: "SshLoginKeyId");

            migrationBuilder.CreateIndex(
                name: "IX_endpoint_accounts_TargetId_Account",
                schema: "access",
                table: "endpoint_accounts",
                columns: new[] { "TargetId", "Account" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_grants_endpoint_accounts_EndpointAccountId",
                schema: "access",
                table: "grants",
                column: "EndpointAccountId",
                principalSchema: "access",
                principalTable: "endpoint_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_pre_registered_grants_endpoint_accounts_EndpointAccountId",
                schema: "access",
                table: "pre_registered_grants",
                column: "EndpointAccountId",
                principalSchema: "access",
                principalTable: "endpoint_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_grants_endpoint_accounts_EndpointAccountId",
                schema: "access",
                table: "grants");

            migrationBuilder.DropForeignKey(
                name: "FK_pre_registered_grants_endpoint_accounts_EndpointAccountId",
                schema: "access",
                table: "pre_registered_grants");

            migrationBuilder.DropTable(
                name: "endpoint_accounts",
                schema: "access");

            migrationBuilder.DropIndex(
                name: "IX_pre_registered_grants_EndpointAccountId",
                schema: "access",
                table: "pre_registered_grants");

            migrationBuilder.DropIndex(
                name: "IX_pre_registered_grants_Issuer_UserName_EndpointAccountId",
                schema: "access",
                table: "pre_registered_grants");

            migrationBuilder.DropIndex(
                name: "IX_grants_EndpointAccountId",
                schema: "access",
                table: "grants");

            migrationBuilder.DropIndex(
                name: "IX_grants_IdentityId_EndpointAccountId",
                schema: "access",
                table: "grants");

            migrationBuilder.DropColumn(
                name: "EndpointAccountId",
                schema: "access",
                table: "pre_registered_grants");

            migrationBuilder.DropColumn(
                name: "EndpointAccountId",
                schema: "access",
                table: "grants");

            migrationBuilder.CreateIndex(
                name: "IX_pre_registered_grants_Issuer_UserName_TargetId",
                schema: "access",
                table: "pre_registered_grants",
                columns: new[] { "Issuer", "UserName", "TargetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_grants_IdentityId_TargetId",
                schema: "access",
                table: "grants",
                columns: new[] { "IdentityId", "TargetId" },
                unique: true);
        }
    }
}

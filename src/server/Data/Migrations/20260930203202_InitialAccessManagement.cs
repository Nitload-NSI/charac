using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Charac.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialAccessManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "access");

            migrationBuilder.CreateTable(
                name: "identities",
                schema: "access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Issuer = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "user_certificate_authorities",
                schema: "access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PublicKey = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    SigningKeyReference = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_certificate_authorities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "targets",
                schema: "access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Address = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    UserCertificateAuthorityId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_targets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_targets_user_certificate_authorities_UserCertificateAuthori~",
                        column: x => x.UserCertificateAuthorityId,
                        principalSchema: "access",
                        principalTable: "user_certificate_authorities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "grants",
                schema: "access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IdentityId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Account = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CertificatePrincipal = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_grants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_grants_identities_IdentityId",
                        column: x => x.IdentityId,
                        principalSchema: "access",
                        principalTable: "identities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_grants_targets_TargetId",
                        column: x => x.TargetId,
                        principalSchema: "access",
                        principalTable: "targets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "host_keys",
                schema: "access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicKey = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_host_keys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_host_keys_targets_TargetId",
                        column: x => x.TargetId,
                        principalSchema: "access",
                        principalTable: "targets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_grants_IdentityId_TargetId",
                schema: "access",
                table: "grants",
                columns: new[] { "IdentityId", "TargetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_grants_TargetId",
                schema: "access",
                table: "grants",
                column: "TargetId");

            migrationBuilder.CreateIndex(
                name: "IX_host_keys_TargetId_PublicKey",
                schema: "access",
                table: "host_keys",
                columns: new[] { "TargetId", "PublicKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_identities_Issuer_Subject",
                schema: "access",
                table: "identities",
                columns: new[] { "Issuer", "Subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_targets_Name",
                schema: "access",
                table: "targets",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_targets_UserCertificateAuthorityId",
                schema: "access",
                table: "targets",
                column: "UserCertificateAuthorityId");

            migrationBuilder.CreateIndex(
                name: "IX_user_certificate_authorities_Name",
                schema: "access",
                table: "user_certificate_authorities",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "grants",
                schema: "access");

            migrationBuilder.DropTable(
                name: "host_keys",
                schema: "access");

            migrationBuilder.DropTable(
                name: "identities",
                schema: "access");

            migrationBuilder.DropTable(
                name: "targets",
                schema: "access");

            migrationBuilder.DropTable(
                name: "user_certificate_authorities",
                schema: "access");
        }
    }
}

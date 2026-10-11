using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DESD.LifeHub.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyLicensePool : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "company_licenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OpenLicensdLicenseId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    LicenseKeyCiphertext = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    LicenseKeyHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    LicenseKeyPrefix = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ProductCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    PolicyName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MaxActivations = table.Column<int>(type: "integer", nullable: true),
                    Revoked = table.Column<bool>(type: "boolean", nullable: false),
                    AssignedToUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_licenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_company_licenses_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_company_licenses_users_AssignedToUserId",
                        column: x => x.AssignedToUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_company_licenses_AssignedToUserId",
                table: "company_licenses",
                column: "AssignedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_company_licenses_LicenseKeyHash",
                table: "company_licenses",
                column: "LicenseKeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_company_licenses_OrganizationId_AssignedToUserId",
                table: "company_licenses",
                columns: new[] { "OrganizationId", "AssignedToUserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "company_licenses");
        }
    }
}

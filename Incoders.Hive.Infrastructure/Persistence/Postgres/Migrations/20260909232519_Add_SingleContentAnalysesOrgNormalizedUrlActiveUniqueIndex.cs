using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Add_SingleContentAnalysesOrgNormalizedUrlActiveUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ux_single_content_analyses_org_normalized_url_active",
                schema: "social",
                table: "single_content_analyses",
                columns: new[] { "organization_id", "normalized_url" },
                unique: true,
                filter: "status NOT IN ('Analyzed', 'Failed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_single_content_analyses_org_normalized_url_active",
                schema: "social",
                table: "single_content_analyses");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialStudio.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Add_IntegrationProvider_MultiCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Add the new categories column (default to an empty array so the NOT NULL add succeeds).
            migrationBuilder.AddColumn<string>(
                name: "categories_json",
                schema: "platform",
                table: "integration_providers",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            // 2. Backfill categories from the single category value: "AiModel" -> ["AiModel"].
            migrationBuilder.Sql(
                "UPDATE platform.integration_providers " +
                "SET categories_json = json_build_array(category)::text;");

            // 3. Rename the flat models column; its content is still a JSON array at this point.
            migrationBuilder.RenameColumn(
                name: "available_models_json",
                schema: "platform",
                table: "integration_providers",
                newName: "models_by_category_json");

            // 4. Wrap the flat model array into a per-category object keyed by the existing category:
            //    ["gpt-4.1"] -> {"AiModel": ["gpt-4.1"]}. category still exists here.
            migrationBuilder.Sql(
                "UPDATE platform.integration_providers " +
                "SET models_by_category_json = json_build_object(" +
                "category, COALESCE(NULLIF(models_by_category_json, ''), '[]')::json)::text;");

            // 5. Drop the now-redundant single category column.
            migrationBuilder.DropColumn(
                name: "category",
                schema: "platform",
                table: "integration_providers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 1. Re-add the single category column.
            migrationBuilder.AddColumn<string>(
                name: "category",
                schema: "platform",
                table: "integration_providers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            // 2. Backfill category from the first declared category in the array.
            migrationBuilder.Sql(
                "UPDATE platform.integration_providers " +
                "SET category = COALESCE(categories_json::json ->> 0, '');");

            // 3. Collapse the per-category model object back to the flat array for the primary category.
            migrationBuilder.Sql(
                "UPDATE platform.integration_providers " +
                "SET models_by_category_json = COALESCE(" +
                "models_by_category_json::json -> category, '[]'::json)::text;");

            // 4. Rename the column back to the flat models column.
            migrationBuilder.RenameColumn(
                name: "models_by_category_json",
                schema: "platform",
                table: "integration_providers",
                newName: "available_models_json");

            // 5. Drop the categories array column.
            migrationBuilder.DropColumn(
                name: "categories_json",
                schema: "platform",
                table: "integration_providers");
        }
    }
}

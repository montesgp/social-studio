using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Add_BackgroundSourcingPolicy_And_ColorRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // (a) Additive relational column: background_sourcing_policy smallint NULL on social.clients.
            // NULL == Unset == legacy 3-tier behavior; no backfill required.
            migrationBuilder.AddColumn<short>(
                name: "background_sourcing_policy",
                schema: "social",
                table: "clients",
                type: "smallint",
                nullable: true);

            // (b) Jsonb shape evolution: backfill role / rank on existing color_palette documents
            // in social.client_brand_manuals.
            //
            // The ColorEntry jsonb record currently may carry a free-text "Role" string or no
            // Role/Rank at all. This step:
            //   1. Maps free-text role to enum ordinal: primary→0, secondary→1, accent→2, neutral→3.
            //      Unrecognized / null strings are mapped to null (dropped, per design §3.2).
            //   2. Assigns rank = array index (0-based) to preserve the current display order.
            //   3. Is idempotent: if a document already has a numeric Role and a Rank the values
            //      are rewritten to themselves (numeric role already in range maps to itself via
            //      the CASE; an existing integer Rank is overwritten with the same array index).
            //
            // Safe to run multiple times. Documents with no color_palette entries are unaffected.
            migrationBuilder.Sql("""
                UPDATE social.client_brand_manuals
                SET color_palette = (
                    SELECT jsonb_agg(
                        jsonb_set(
                            jsonb_set(
                                entry,
                                '{Rank}',
                                to_jsonb(entry_idx.idx)
                            ),
                            '{Role}',
                            CASE
                                WHEN lower(entry->>'Role') IN ('primary', '0')   THEN '0'::jsonb
                                WHEN lower(entry->>'Role') IN ('secondary', '1') THEN '1'::jsonb
                                WHEN lower(entry->>'Role') IN ('accent', '2')    THEN '2'::jsonb
                                WHEN lower(entry->>'Role') IN ('neutral', '3')   THEN '3'::jsonb
                                ELSE 'null'::jsonb
                            END
                        )
                    )
                    FROM jsonb_array_elements(color_palette) WITH ORDINALITY AS entry_idx(entry, idx)
                )
                WHERE color_palette IS NOT NULL
                  AND jsonb_array_length(color_palette) > 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "background_sourcing_policy",
                schema: "social",
                table: "clients");
        }
    }
}

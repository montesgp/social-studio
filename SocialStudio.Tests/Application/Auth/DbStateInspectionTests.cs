using Npgsql;

namespace SocialStudio.Tests.Application.Auth;

/// <summary>
/// Manual diagnostics: hit the real Postgres to dump migrations history and the state of the
/// `identity` schema. Filtered out of the default suite — invoke with:
/// <c>dotnet test --filter "Category=DbInspect"</c>.
/// Reads the connection string from the SYSADMIN_DB_CONNECTION_STRING environment variable so
/// the test never compiles credentials in.
/// </summary>
public class DbStateInspectionTests
{
    private const string EnvVar = "SYSADMIN_DB_CONNECTION_STRING";

    [Fact(Skip = "Manual diagnostics only. Requires SYSADMIN_DB_CONNECTION_STRING and explicit local execution.")]
    [Trait("Category", "DbInspect")]
    public async Task DumpIdentityState()
    {
        var cs = Environment.GetEnvironmentVariable(EnvVar);
        Assert.False(string.IsNullOrWhiteSpace(cs),
            $"Set {EnvVar} to the Postgres connection string before running this test.");

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        var summary = new System.Text.StringBuilder();

        summary.AppendLine("=== __ef_migrations_history locations across schemas ===");
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT table_schema, table_name FROM information_schema.tables
                                WHERE table_name = '__ef_migrations_history'
                                ORDER BY table_schema";
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) summary.AppendLine($"  {r.GetString(0)}.{r.GetString(1)}");
        }

        summary.AppendLine();
        summary.AppendLine("=== Migrations applied (each found history table) ===");
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT table_schema FROM information_schema.tables
                                WHERE table_name = '__ef_migrations_history'";
            var schemas = new List<string>();
            await using (var r = await cmd.ExecuteReaderAsync())
                while (await r.ReadAsync()) schemas.Add(r.GetString(0));

            foreach (var sch in schemas)
            {
                summary.AppendLine($"  --- {sch}.__ef_migrations_history ---");
                await using var c2 = conn.CreateCommand();
                c2.CommandText = $"SELECT \"MigrationId\" FROM {sch}.__ef_migrations_history ORDER BY \"MigrationId\"";
                await using var r2 = await c2.ExecuteReaderAsync();
                while (await r2.ReadAsync()) summary.AppendLine("    " + r2.GetString(0));
            }
        }

        summary.AppendLine();
        summary.AppendLine("=== Tables in 'identity' schema ===");
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema = 'identity' ORDER BY table_name";
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) summary.AppendLine("  " + r.GetString(0));
        }

        summary.AppendLine();
        summary.AppendLine("=== Row counts in identity.* ===");
        foreach (var t in new[] { "roles", "users", "user_roles", "user_claims", "user_logins", "user_tokens", "role_claims" })
        {
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM identity.{t}";
                var count = await cmd.ExecuteScalarAsync();
                summary.AppendLine($"  identity.{t} = {count}");
            }
            catch (Exception ex)
            {
                summary.AppendLine($"  identity.{t} : ERROR — {ex.Message.Split('\n')[0]}");
            }
        }

        summary.AppendLine();
        summary.AppendLine("=== Users in identity.users ===");
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, email, normalized_email, email_confirmed, user_name FROM identity.users";
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                summary.AppendLine(
                    $"  id={r.GetGuid(0)} email={r["email"]} confirmed={r["email_confirmed"]} username={r["user_name"]}");
            }
        }
        catch (Exception ex) { summary.AppendLine("  ERROR: " + ex.Message.Split('\n')[0]); }

        summary.AppendLine();
        summary.AppendLine("=== Columns of identity.roles (if exists) ===");
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT column_name, data_type
                                FROM information_schema.columns
                                WHERE table_schema = 'identity' AND table_name = 'roles'
                                ORDER BY ordinal_position";
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) summary.AppendLine($"  {r.GetString(0)} : {r.GetString(1)}");
        }
        catch (Exception ex)
        {
            summary.AppendLine("  ERROR: " + ex.Message.Split('\n')[0]);
        }

        // Force xUnit to surface the output by failing with the diagnostic content.
        // (xUnit suppresses Console output by default.)
        Assert.Fail(summary.ToString());
    }

    /// <summary>
    /// Marks the migrations whose state is already materialized in the DB as applied, without
    /// running their SQL. Use ONLY after manual inspection has confirmed the destination state
    /// matches the migrations' intent.
    /// </summary>
    [Fact(Skip = "Manual repair only. Requires SYSADMIN_DB_CONNECTION_STRING and explicit local execution.")]
    [Trait("Category", "DbRepair")]
    public async Task MarkPendingMigrationsAsApplied()
    {
        var cs = Environment.GetEnvironmentVariable(EnvVar);
        Assert.False(string.IsNullOrWhiteSpace(cs),
            $"Set {EnvVar} to the Postgres connection string before running this test.");

        var pendingToMark = new[]
        {
            "20260516025427_UseIdentitySchemaSnakeCase",
            "20260516035531_MovePlatformTablesToPlatformSchema",
            "20260517000000_RemoveUserPreferencesUserFk",
            "20260517010000_DropIdentityTables",
            "20260516183221_ReintroduceIdentityTables",
        };

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        var summary = new System.Text.StringBuilder();
        foreach (var mid in pendingToMark)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO public.__ef_migrations_history (""MigrationId"", ""ProductVersion"")
                                VALUES (@id, '10.0.0')
                                ON CONFLICT (""MigrationId"") DO NOTHING
                                RETURNING ""MigrationId""";
            cmd.Parameters.AddWithValue("id", mid);
            var inserted = await cmd.ExecuteScalarAsync();
            summary.AppendLine(inserted is null
                ? $"  {mid}: already present"
                : $"  {mid}: marked applied");
        }

        Assert.Fail(summary.ToString());
    }
}

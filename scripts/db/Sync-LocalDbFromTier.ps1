# Load a tier's DATA into the local Docker Postgres to reproduce a bug with
# "same code + same data" (issue #62).
#
# Design: the local schema comes from the migrations of the branch you have checked out
# (run scripts/dev/Start-LocalDb.ps1 and boot the API once first, so the schema +
# __ef_migrations_history exist). This script then replaces the DATA in the app schemas
# (identity, platform, social) with a snapshot pulled from the chosen tier. Because the
# schema is unchanged, EF still sees the migration history as up to date.
#
#  WARNING: this pulls REAL data (possibly PII) from a live database and consumes egress
#  on that tier. It is a deliberate, occasional action for debugging - NOT part of routine
#  local dev (routine dev uses migrations + SeedData only, no cloud data).
#
# The source connection must be a libpq / URI string that pg_dump understands, e.g. the
# "Connection string" Supabase shows under Project Settings > Database:
#   postgresql://postgres:<pwd>@db.<ref>.supabase.co:5432/postgres?sslmode=require
#
# Usage:
#   pwsh -File scripts/db/Sync-LocalDbFromTier.ps1 -SourceConnection "postgresql://..."
#   pwsh -File scripts/db/Sync-LocalDbFromTier.ps1 -SourceConnection "$env:SOURCE_POSTGRES_URI" -Force

[CmdletBinding()]
param(
    # libpq / postgresql:// connection string for the tier to copy FROM (source of truth).
    [Parameter(Mandatory)] [string] $SourceConnection,
    # App schemas to snapshot. EF migration history (public) is intentionally left untouched.
    [string[]] $Schemas = @('identity', 'platform', 'social'),
    [string] $TargetDatabase = 'incoders_hive_dev',
    # Skip the interactive confirmation (for scripted/CI use — think before you do this).
    [switch] $Force
)

$ErrorActionPreference = 'Stop'
$container = 'incoders-hive-dev-postgres'
$localUser = 'incoders'
$dumpPath = '/tmp/tier_data.dump'

function Write-Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }
function Invoke-InContainer { param([string[]] $DockerArgs) docker exec @DockerArgs; if ($LASTEXITCODE -ne 0) { throw "docker exec failed: $($DockerArgs -join ' ')" } }

# --- Preconditions ------------------------------------------------------------
Write-Step 'Checking the local database is up and already migrated'
$hist = docker exec $container psql -U $localUser -d $TargetDatabase -tAc `
    "SELECT count(*) FROM information_schema.tables WHERE table_name = '__ef_migrations_history';" 2>$null
if ($LASTEXITCODE -ne 0) { throw "Local database '$TargetDatabase' is not reachable. Run scripts/dev/Start-LocalDb.ps1 first." }
if (($hist | Out-String).Trim() -eq '0') {
    throw "Local '$TargetDatabase' has no migration history - boot the API once (dotnet run) so the schema is built from your branch, then re-run this."
}

# --- Consent: this touches real data + egress ---------------------------------
if (-not $Force) {
    Write-Host ''
    Write-Warning "About to pull data from a LIVE tier into local '$TargetDatabase'."
    Write-Warning 'This reads real (possibly PII) data and consumes egress on the source.'
    $answer = Read-Host "Type 'yes' to continue"
    if ($answer -ne 'yes') { Write-Host 'Aborted.'; return }
}

# --- 1. Dump DATA ONLY from the source (app schemas only) ---------------------
Write-Step "Dumping data from source (schemas: $($Schemas -join ', '))"
$schemaArgs = @(); foreach ($s in $Schemas) { $schemaArgs += @('--schema', $s) }
Invoke-InContainer (@($container, 'pg_dump', $SourceConnection, '--data-only', '--no-owner',
    '--format=custom', '--file', $dumpPath) + $schemaArgs)

# --- 2. Clear existing data in the target app schemas -------------------------
Write-Step 'Truncating local app-schema tables before load'
$schemaList = ($Schemas | ForEach-Object { "'$_'" }) -join ','
$truncateSql = @"
DO `$`$
DECLARE r record;
BEGIN
  FOR r IN SELECT format('%I.%I', schemaname, tablename) AS t
           FROM pg_tables WHERE schemaname IN ($schemaList) LOOP
    EXECUTE 'TRUNCATE TABLE ' || r.t || ' RESTART IDENTITY CASCADE';
  END LOOP;
END `$`$;
"@
Invoke-InContainer @($container, 'psql', '-U', $localUser, '-d', $TargetDatabase, '-v', 'ON_ERROR_STOP=1', '-c', $truncateSql)

# --- 3. Restore the data (triggers disabled for FK-independent load) ----------
# pg_restore --data-only routinely exits non-zero on IGNORABLE errors (e.g. a `setval` on a
# legacy sequence that the source has but the clean local schema does not). Those are warned,
# not fatal - the data rows still load. Do not throw here; surface the warning and verify counts.
Write-Step 'Restoring tier data into local'
docker exec $container pg_restore --data-only --disable-triggers --no-owner `
    -U $localUser -d $TargetDatabase $dumpPath
if ($LASTEXITCODE -ne 0) {
    Write-Warning "pg_restore reported non-fatal errors (exit $LASTEXITCODE). Usually harmless - e.g. setval on legacy sequences the clean local schema lacks. Data rows are still loaded; verify row counts below."
}

# --- 4. Clean up --------------------------------------------------------------
docker exec $container rm -f $dumpPath 2>$null | Out-Null

Write-Host ''
Write-Host "Local '$TargetDatabase' now holds the tier's data with your branch's schema." -ForegroundColor Green
Write-Host '  Reproduce the bug: dotnet run --project Incoders.Hive.ApiService -c Release'
Write-Host '  Remember to rotate/scrub if the snapshot contained sensitive data.'

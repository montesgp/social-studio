# Turnkey local dev database bootstrap (issue #62).
#
# Brings up the disposable local Docker Postgres and guards against the #1 footgun:
# a `dotnet user-secrets` entry that points the app at a shared/cloud database, which
# silently overrides appsettings.Development.json and makes "local" dev hit production.
#
# Usage:
#   pwsh -File scripts/dev/Start-LocalDb.ps1            # up + safety check
#   pwsh -File scripts/dev/Start-LocalDb.ps1 -Recreate  # wipe the volume and start fresh
#
# After this, run the API (it auto-migrates from empty + seeds in Development):
#   dotnet run --project Incoders.Hive.ApiService -c Release

[CmdletBinding()]
param(
    # Drop the named volume so the next start is a truly empty database.
    [switch] $Recreate
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..' '..')
$compose = Join-Path $repoRoot 'docker-compose.dev.yml'
$localConn = 'Host=localhost;Port=5433;Database=incoders_hive_dev;Username=incoders;Password=incoders_dev_only'
$apiProject = Join-Path $repoRoot 'Incoders.Hive.ApiService' 'Incoders.Hive.ApiService.csproj'

function Write-Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }

# --- Docker availability ------------------------------------------------------
Write-Step 'Checking Docker'
try { docker info *> $null } catch { throw 'Docker is not running. Start Docker Desktop (needs CPU virtualization / VT-x / SVM enabled) and retry.' }

# --- Bring the disposable Postgres up -----------------------------------------
if ($Recreate) {
    Write-Step 'Recreate requested - wiping the local DB volume'
    docker compose -f $compose down -v 2>&1 | Out-Host
}
Write-Step 'Starting local Postgres (docker-compose.dev.yml)'
docker compose -f $compose up -d 2>&1 | Out-Host

# --- Wait for health ----------------------------------------------------------
Write-Step 'Waiting for Postgres to become reachable'
$ready = $false
foreach ($i in 1..30) {
    $state = (docker inspect --format '{{.State.Health.Status}}' incoders-hive-dev-postgres 2>$null)
    if ($state -eq 'healthy') { $ready = $true; break }
    Start-Sleep -Seconds 2
}
if (-not $ready) { throw 'Postgres did not become healthy in time. Check: docker logs incoders-hive-dev-postgres' }
Write-Host '    Postgres is healthy on 127.0.0.1:5433' -ForegroundColor Green

# --- Safety check: no shared/cloud DB in user-secrets -------------------------
# user-secrets OVERRIDE appsettings.Development.json. A cloud connection string here
# makes local runs silently hit that database. See issue #62.
Write-Step 'Safety check: user-secrets must not point the app at a shared/cloud DB'
$secretConn = $null
try {
    $secrets = dotnet user-secrets list --project $apiProject 2>$null
    $secretConn = ($secrets | Select-String -Pattern '^ConnectionStrings:Postgres\s*=\s*(.+)$').Matches.Groups[1].Value
} catch { $secretConn = $null }

if ($secretConn) {
    $isLocal = $secretConn -match '(?i)(localhost|127\.0\.0\.1)' -and $secretConn -match '5433'
    if (-not $isLocal) {
        Write-Host ''
        Write-Warning 'A user-secret ConnectionStrings:Postgres points at a NON-LOCAL database.'
        Write-Warning 'This OVERRIDES appsettings.Development.json - the app will hit that DB, not local Docker.'
        Write-Host    '    Remove it so local dev uses the local Docker Postgres:' -ForegroundColor Yellow
        Write-Host    "      dotnet user-secrets remove `"ConnectionStrings:Postgres`" --project Incoders.Hive.ApiService" -ForegroundColor Yellow
        Write-Host    '    (Reserve user-secrets for personal API keys only - never the shared DB.)' -ForegroundColor Yellow
        Write-Host ''
        throw 'Refusing to continue: local dev would connect to a non-local database.'
    }
    Write-Host '    user-secrets Postgres connection is local - OK' -ForegroundColor Green
} else {
    Write-Host '    No Postgres user-secret; the app will use appsettings.Development.json (local) - OK' -ForegroundColor Green
}

# --- Done ---------------------------------------------------------------------
Write-Host ''
Write-Host 'Local DB is ready.' -ForegroundColor Green
Write-Host "  Connection: $localConn"
Write-Host '  Run the API (auto-migrates from empty + seeds in Development):'
Write-Host '    dotnet run --project Incoders.Hive.ApiService -c Release'
Write-Host '  To load real data from a tier to reproduce a bug, see scripts/db/Sync-LocalDbFromTier.ps1.'

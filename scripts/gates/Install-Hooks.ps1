# Install-Hooks.ps1
#
# Installs Incoders Quality Gates git hooks into .git/hooks/.
#
# Usage:
#   pwsh -File scripts/gates/Install-Hooks.ps1                # install pre-commit (fast gates, default)
#   pwsh -File scripts/gates/Install-Hooks.ps1 -PrePush       # also install pre-push (full suite)
#   pwsh -File scripts/gates/Install-Hooks.ps1 -Uninstall     # remove our hooks
#   pwsh -File scripts/gates/Install-Hooks.ps1 -Force         # overwrite existing non-gate hooks
#
# Hooks live tracked under scripts/gates/hooks/ and are copied (not symlinked) on install
# so they survive across .git/ rewrites and work uniformly on Windows + macOS + Linux.

[CmdletBinding()]
param(
    [switch] $PrePush,
    [switch] $Uninstall,
    [switch] $Force
)

$ErrorActionPreference = 'Stop'

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$HooksSrc = Join-Path $PSScriptRoot 'hooks'
$HooksDst = Join-Path $RepoRoot '.git\hooks'

if (-not (Test-Path (Join-Path $RepoRoot '.git'))) {
    Write-Host "ERROR: $RepoRoot is not a git repository (missing .git/)." -ForegroundColor Red
    Write-Host "Run 'git init' first, or call this script from a checked-out clone." -ForegroundColor Red
    exit 2
}
if (-not (Test-Path $HooksDst)) { New-Item -ItemType Directory -Path $HooksDst | Out-Null }

# Marker so we recognize our own hooks and don't clobber unrelated ones.
$marker = '# Incoders Quality Gates'

function Test-OurHook([string] $path) {
    if (-not (Test-Path -LiteralPath $path)) { return $false }
    $first = Get-Content -LiteralPath $path -TotalCount 5 -ErrorAction SilentlyContinue
    if ($null -eq $first) { return $false }
    return ($first -join "`n") -match [regex]::Escape($marker)
}

function Install-Hook([string] $name) {
    $src = Join-Path $HooksSrc $name
    $dst = Join-Path $HooksDst $name
    if (-not (Test-Path -LiteralPath $src)) {
        Write-Host ("[skip] {0}: source not found at {1}" -f $name, $src) -ForegroundColor Yellow
        return
    }
    if ((Test-Path -LiteralPath $dst) -and -not (Test-OurHook $dst) -and -not $Force) {
        Write-Host ("[skip] {0}: existing non-gate hook at {1} (use -Force to overwrite)" -f $name, $dst) -ForegroundColor Yellow
        return
    }
    Copy-Item -LiteralPath $src -Destination $dst -Force
    # Ensure exec bit on Unix (no-op on Windows where git for windows ignores it anyway)
    if ($IsLinux -or $IsMacOS) {
        & chmod +x $dst
    }
    Write-Host ("[ok]   {0}: installed -> {1}" -f $name, $dst) -ForegroundColor Green
}

function Uninstall-Hook([string] $name) {
    $dst = Join-Path $HooksDst $name
    if (-not (Test-Path -LiteralPath $dst)) {
        Write-Host ("[skip] {0}: not installed" -f $name) -ForegroundColor DarkGray
        return
    }
    if (-not (Test-OurHook $dst) -and -not $Force) {
        Write-Host ("[skip] {0}: hook is not ours, refusing to remove (use -Force to override)" -f $name) -ForegroundColor Yellow
        return
    }
    Remove-Item -LiteralPath $dst -Force
    Write-Host ("[ok]   {0}: removed" -f $name) -ForegroundColor Green
}

if ($Uninstall) {
    Uninstall-Hook 'pre-commit'
    Uninstall-Hook 'pre-push'
    exit 0
}

Install-Hook 'pre-commit'
if ($PrePush) {
    Install-Hook 'pre-push'
} else {
    Write-Host '[note] pre-push not installed. Re-run with -PrePush to also block pushes on full-suite failure.' -ForegroundColor DarkGray
}

Write-Host ''
Write-Host 'Done. Hooks active for this clone only (.git/hooks/ is not tracked).' -ForegroundColor Cyan
Write-Host 'New clones must run this installer once.' -ForegroundColor Cyan

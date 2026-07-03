# Run-Gates.ps1
#
# Orchestrates Phase 1 quality gates declared in project.gates.yml.
# Usage:
#   pwsh -File scripts/gates/Run-Gates.ps1                  # run all gates
#   pwsh -File scripts/gates/Run-Gates.ps1 -Only build,test
#   pwsh -File scripts/gates/Run-Gates.ps1 -Skip vulnerabilities
#   pwsh -File scripts/gates/Run-Gates.ps1 -Json            # JSON summary on stdout
#   pwsh -File scripts/gates/Run-Gates.ps1 -ConfigPath path/to/project.gates.yml
#
# Exit codes:
#   0   all MUST gates passed
#   1   at least one MUST gate failed
#   2   configuration error

[CmdletBinding()]
param(
    [string] $ConfigPath,
    [string[]] $Only,
    [string[]] $Skip,
    [switch] $Json,
    [switch] $NoSummary,
    [string] $CoverageDir
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'lib\Common.ps1')
. (Join-Path $PSScriptRoot 'lib\Yaml.ps1')

$RepoRoot = Get-RepoRoot
if (-not $ConfigPath) { $ConfigPath = Join-Path $RepoRoot 'project.gates.yml' }
if (-not (Test-Path $ConfigPath)) {
    Write-Host "ERROR: project.gates.yml not found at $ConfigPath" -ForegroundColor Red
    exit 2
}

try {
    $rawConfig = ConvertFrom-Yaml -Path $ConfigPath
} catch {
    Write-Host "ERROR: failed to parse $ConfigPath`n$_" -ForegroundColor Red
    exit 2
}

# Convert to PSCustomObject for property-style access
function ConvertTo-Object($node) {
    if ($null -eq $node) { return $null }
    if ($node -is [System.Collections.IDictionary]) {
        $obj = [pscustomobject]@{}
        foreach ($k in $node.Keys) {
            $obj | Add-Member -NotePropertyName $k -NotePropertyValue (ConvertTo-Object $node[$k])
        }
        return $obj
    } elseif ($node -is [System.Collections.IList] -and -not ($node -is [string])) {
        return ,@($node | ForEach-Object { ConvertTo-Object $_ })
    } else {
        return $node
    }
}
$Config = ConvertTo-Object $rawConfig

# Available gates and their script files (in canonical run order)
$gateScripts = [ordered] @{
    'build'          = 'checks\Check-Build.ps1'
    'tests'          = 'checks\Check-Tests.ps1'
    'coverage'       = 'checks\Check-Coverage.ps1'
    'architecture'   = 'checks\Check-Architecture.ps1'
    'dependencies'   = 'checks\Check-Dependencies.ps1'
    'secrets'        = 'checks\Check-Secrets.ps1'
    'security'       = 'checks\Check-Security.ps1'
    'vulnerabilities'= 'checks\Check-Vulnerabilities.ps1'
}

# Filter
$selected = $gateScripts.Keys
if ($Only) { $selected = $gateScripts.Keys | Where-Object { $Only -contains $_ } }
if ($Skip) { $selected = $selected | Where-Object { $Skip -notcontains $_ } }

if (-not $CoverageDir) { $CoverageDir = Join-Path $RepoRoot '.gates/coverage' }

if (-not $Json) {
    Write-Host ''
    Write-Host '=== Incoders Quality Gates (Phase 1) ===' -ForegroundColor Cyan
    Write-Host "Repo:    $RepoRoot"
    Write-Host "Config:  $ConfigPath"
    Write-Host "Gates:   $($selected -join ', ')"
    Write-Host ''
}

$allResults = @()
foreach ($gate in $selected) {
    $rel = $gateScripts[$gate]
    $script = Join-Path $PSScriptRoot $rel
    if (-not (Test-Path $script)) {
        $allResults += New-GateResult -Gate $gate -Level 'MUST' -Status 'fail' `
            -Message "missing script: $rel"
        continue
    }
    try {
        $r = & $script -Config $Config -RepoRoot $RepoRoot -CoverageDir $CoverageDir -ErrorAction Stop
        if ($null -eq $r) { $r = @() }
        if ($r -isnot [array]) { $r = @($r) }
        foreach ($x in $r) {
            $allResults += $x
            if (-not $Json) { Write-GateResult -Result $x }
        }
    } catch {
        $err = New-GateResult -Gate $gate -Level 'MUST' -Status 'fail' `
            -Message "exception: $($_.Exception.Message)" `
            -Details $_.ScriptStackTrace
        $allResults += $err
        if (-not $Json) { Write-GateResult -Result $err }
    }
}

if ($Json) {
    ConvertTo-SafeGateResultsJson -Results $allResults
} elseif (-not $NoSummary) {
    Write-GateSummary -Results $allResults
}

exit (Get-AggregateExitCode -Results $allResults)

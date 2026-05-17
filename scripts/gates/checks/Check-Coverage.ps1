# Coverage gate: parse coverlet cobertura output and report line-coverage percent.

[CmdletBinding()]
param(
    [Parameter(Mandatory)] $Config,
    [string] $RepoRoot,
    [string] $CoverageDir
)

. (Join-Path $PSScriptRoot '..\lib\Common.ps1')

$results = @()
$cfg = $Config.gates.coverage
$level = if ($cfg -and $cfg.level) { $cfg.level } else { 'SHOULD' }
$min   = if ($cfg -and $cfg.PSObject.Properties.Name -contains 'minimum') { [int] $cfg.minimum } else { 0 }
$target = if ($cfg -and $cfg.PSObject.Properties.Name -contains 'target') { [int] $cfg.target } else { 0 }

if (-not $CoverageDir) { $CoverageDir = Join-Path $RepoRoot '.gates/coverage' }
if (-not (Test-Path $CoverageDir)) {
    $results += New-GateResult -Gate 'coverage.line' -Level $level -Status 'skipped' `
        -Message 'no coverage directory yet (tests not run with coverage?)'
    return ,$results
}

$reports = Get-ChildItem -Path $CoverageDir -Recurse -Filter 'coverage.cobertura.xml' -ErrorAction SilentlyContinue
if (-not $reports) {
    $results += New-GateResult -Gate 'coverage.line' -Level $level -Status 'skipped' `
        -Message "no coverage.cobertura.xml under $CoverageDir"
    return ,$results
}

$totalLines = 0
$coveredLines = 0
foreach ($r in $reports) {
    try {
        [xml] $xml = Get-Content -LiteralPath $r.FullName
        $rate = [double] $xml.coverage.'line-rate'
        # cobertura reports line-rate at root + at package level. Prefer aggregating from packages.
        $packages = $xml.coverage.packages.package
        foreach ($p in $packages) {
            foreach ($cls in $p.classes.class) {
                foreach ($ln in $cls.lines.line) {
                    $totalLines++
                    if ([int] $ln.hits -gt 0) { $coveredLines++ }
                }
            }
        }
    } catch {
        # ignore individual file errors; fall back to root rate if accumulator is zero
    }
}

if ($totalLines -eq 0) {
    # Fallback: use root line-rate from the first report
    try {
        [xml] $xml = Get-Content -LiteralPath $reports[0].FullName
        $rate = [double] $xml.coverage.'line-rate'
        $pct = [math]::Round($rate * 100, 1)
    } catch {
        $results += New-GateResult -Gate 'coverage.line' -Level $level -Status 'warn' `
            -Message 'coverage report unreadable'
        return ,$results
    }
} else {
    $pct = [math]::Round(($coveredLines / [double] $totalLines) * 100, 1)
}

$status = 'pass'
$msg = "{0}% (min={1}%, target={2}%)" -f $pct, $min, $target
if ($pct -lt $min) { $status = 'fail' }
elseif ($target -gt 0 -and $pct -lt $target) { $status = 'warn' }

$results += New-GateResult -Gate 'coverage.line' -Level $level -Status $status -Message $msg

# Pointer
$first = $reports | Select-Object -First 1
$rel = $first.FullName.Replace($RepoRoot, '').TrimStart('\','/')
$results += New-GateResult -Gate 'coverage.report' -Level 'MAY' -Status 'pass' -Message $rel

return ,$results

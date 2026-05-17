# Vulnerabilities gate: dotnet list package --vulnerable + npm audit (per app).
# Default level: SHOULD (warn-only). Promote to MUST in Phase 2 once baseline is clean.

[CmdletBinding()]
param(
    [Parameter(Mandatory)] $Config,
    [string] $RepoRoot,
    [string] $CoverageDir   # unused, accepted for orchestrator uniformity
)

. (Join-Path $PSScriptRoot '..\lib\Common.ps1')

$results = @()
$cfg = $Config.gates.vulnerabilities
$level = if ($cfg -and $cfg.level) { $cfg.level } else { 'SHOULD' }
$failOn = @()
if ($cfg -and $cfg.fail_on) { $failOn = @($cfg.fail_on) }

# ---- Backend ----
if ($Config.backend -and $Config.backend.enabled) {
    if (-not (Test-CommandAvailable 'dotnet')) {
        $results += New-GateResult -Gate 'vulnerabilities.backend' -Level $level -Status 'skipped' `
            -Message 'dotnet SDK not available'
    } else {
        $solPath = Join-Path $RepoRoot $Config.backend.solution
        $r = Invoke-External -FilePath 'dotnet' -ArgumentList @(
            'list', $solPath, 'package', '--vulnerable', '--include-transitive'
        ) -WorkingDirectory $RepoRoot
        if ($r.ExitCode -ne 0) {
            $results += New-GateResult -Gate 'vulnerabilities.backend' -Level $level -Status 'warn' `
                -Message "dotnet list package --vulnerable exited $($r.ExitCode)" `
                -Details ($r.StdOut + "`n" + $r.StdErr)
        } else {
            $hasFindings = $r.StdOut -match '>\s*\S+\s+\S+\s+\S+\s+(Low|Moderate|High|Critical)'
            if ($hasFindings) {
                $sevs = ([regex]::Matches($r.StdOut, '\b(Low|Moderate|High|Critical)\b') | ForEach-Object { $_.Value } | Group-Object | ForEach-Object { "$($_.Name)=$($_.Count)" }) -join ', '
                $stat = 'warn'
                foreach ($f in $failOn) {
                    if ($r.StdOut -match "\b$f\b") { $stat = 'fail'; break }
                }
                $results += New-GateResult -Gate 'vulnerabilities.backend' -Level $level -Status $stat `
                    -Message "vulnerable packages: $sevs" -Details $r.StdOut
            } else {
                $results += New-GateResult -Gate 'vulnerabilities.backend' -Level $level -Status 'pass' `
                    -Message 'no vulnerable packages reported'
            }
        }
    }
}

# ---- Frontend ----
if ($Config.frontend -and $Config.frontend.enabled -and $Config.frontend.apps) {
    foreach ($app in $Config.frontend.apps) {
        $name = $app.name
        $appPath = Join-Path $RepoRoot $app.path
        $pm = $app.package_manager
        if (-not $pm) { $pm = 'npm' }
        if (-not (Test-CommandAvailable $pm)) {
            $results += New-GateResult -Gate "vulnerabilities.frontend.$name" -Level $level -Status 'skipped' `
                -Message "$pm not available"
            continue
        }
        if (-not (Test-Path $appPath)) {
            $results += New-GateResult -Gate "vulnerabilities.frontend.$name" -Level $level -Status 'skipped' `
                -Message "app path missing: $($app.path)"
            continue
        }
        $r = Invoke-External -FilePath $pm -ArgumentList @(
            'audit','--omit=dev','--audit-level=high','--json'
        ) -WorkingDirectory $appPath
        # npm audit exits non-zero when findings are at >= audit-level. Parse JSON for accurate counts.
        $high = 0; $crit = 0
        try {
            $j = $r.StdOut | ConvertFrom-Json -ErrorAction Stop
            if ($j.metadata -and $j.metadata.vulnerabilities) {
                $high = [int] $j.metadata.vulnerabilities.high
                $crit = [int] $j.metadata.vulnerabilities.critical
            }
        } catch {
            # network or shape issue; still emit a warn
            $results += New-GateResult -Gate "vulnerabilities.frontend.$name" -Level $level -Status 'warn' `
                -Message ("$pm audit unreadable (exit=$($r.ExitCode))") `
                -Details ($r.StdOut + "`n" + $r.StdErr)
            continue
        }
        $stat = 'pass'
        if (($high + $crit) -gt 0) {
            $stat = 'warn'
            foreach ($f in $failOn) {
                if ($f -ieq 'high' -and $high -gt 0)     { $stat = 'fail' }
                if ($f -ieq 'critical' -and $crit -gt 0) { $stat = 'fail' }
            }
        }
        $results += New-GateResult -Gate "vulnerabilities.frontend.$name" -Level $level -Status $stat `
            -Message ("high={0}, critical={1}" -f $high, $crit)
    }
}

return ,$results

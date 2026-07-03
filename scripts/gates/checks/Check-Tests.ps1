# Test gate: dotnet test on each declared test project, plus optional frontend test_script.

[CmdletBinding()]
param(
    [Parameter(Mandatory)] $Config,
    [string] $RepoRoot,
    [string] $CoverageDir
)

. (Join-Path $PSScriptRoot '..\lib\Common.ps1')

$results = @()
$level = $Config.gates.test.level
if (-not $level) { $level = 'MUST' }
# Coverage collection is opt-in. Some SDK + coverlet combinations crash tests
# when XPlat Code Coverage is collected; let the project decide.
$collectCoverage = $false
if ($Config.gates.test -and $Config.gates.test.PSObject.Properties.Name -contains 'collect_coverage') {
    $collectCoverage = [bool] $Config.gates.test.collect_coverage
}

# ---- Backend ----
if ($Config.backend -and $Config.backend.enabled -and $Config.backend.test_projects) {
    if (-not (Test-CommandAvailable 'dotnet')) {
        $results += New-GateResult -Gate 'tests.backend' -Level $level -Status 'fail' `
            -Message 'dotnet SDK not found on PATH'
    } else {
        if (-not $CoverageDir) { $CoverageDir = Join-Path $RepoRoot '.gates/coverage' }
        if (-not (Test-Path $CoverageDir)) { New-Item -ItemType Directory -Path $CoverageDir | Out-Null }

        foreach ($proj in $Config.backend.test_projects) {
            $projPath = Join-Path $RepoRoot $proj
            if (-not (Test-Path $projPath)) {
                $results += New-GateResult -Gate "tests.backend" -Level $level -Status 'fail' `
                    -Message "Test project not found: $proj"
                continue
            }
            $sw = [System.Diagnostics.Stopwatch]::StartNew()
            $testArtifacts = Get-TestArtifactPaths `
                -RepoRoot $RepoRoot `
                -CoverageDirectory $CoverageDir `
                -ProjectPath ([string] $proj) `
                -CollectCoverage $collectCoverage
            $trxDirectory = $testArtifacts.Directory
            if (-not (Test-Path -LiteralPath $trxDirectory)) {
                New-Item -ItemType Directory -Path $trxDirectory -Force | Out-Null
            }
            $trxFileName = $testArtifacts.TrxFileName
            $trxPath = $testArtifacts.TrxPath
            if (Test-Path -LiteralPath $trxPath) { Remove-Item -LiteralPath $trxPath -Force }
            $dotnetArgs = Get-DotNetTestArguments `
                -ProjectPath $projPath `
                -TrxFileName $trxFileName `
                -ResultsDirectory $trxDirectory `
                -CollectCoverage $collectCoverage
            $test = Invoke-External -FilePath 'dotnet' -ArgumentList $dotnetArgs -WorkingDirectory $RepoRoot
            $sw.Stop()
            $stat = 'fail'
            if ($test.ExitCode -eq 0) { $stat = 'pass' }
            # dotnet test 10 emits "Total tests: N", "Passed: N", "Failed: N" (only when > 0),
            # "Skipped: N" (only when > 0) on separate lines.
            $total = $null
            if ($test.StdOut -match '(?m)^\s*Total tests:\s+(\d+)') { $total = [int] $Matches[1] }
            $passed = 0
            if ($test.StdOut -match '(?m)^\s*Passed:\s+(\d+)') { $passed = [int] $Matches[1] }
            $failed = 0
            if ($test.StdOut -match '(?m)^\s*Failed:\s+(\d+)') { $failed = [int] $Matches[1] }
            $skipped = 0
            if ($test.StdOut -match '(?m)^\s*Skipped:\s+(\d+)') { $skipped = [int] $Matches[1] }
            $totals = ''
            if ($null -ne $total) {
                $totals = " (total=$total, passed=$passed, failed=$failed, skipped=$skipped)"
            }
            $details = $null
            if ($stat -eq 'fail') {
                $failureSummary = Get-TrxFailureSummary -Path $trxPath
                $details = if ($failureSummary) {
                    "=== STRUCTURED TEST FAILURES ===`n$failureSummary`n=== CONSOLE CONTEXT ===`n$($test.StdOut)`n$($test.StdErr)"
                } else {
                    "=== TRX FAILURE SUMMARY UNAVAILABLE ===`n=== CONSOLE CONTEXT ===`n$($test.StdOut)`n$($test.StdErr)"
                }
            }
            $results += New-GateResult -Gate 'tests.backend' -Level $level -Status $stat `
                -Message ("{0} in {1:N1}s{2}" -f (Split-Path $proj -Leaf), $sw.Elapsed.TotalSeconds, $totals) `
                -Details $details
        }
    }
}

# ---- Frontend (optional) ----
if ($Config.frontend -and $Config.frontend.enabled -and $Config.frontend.apps) {
    foreach ($app in $Config.frontend.apps) {
        if (-not $app.test_script) {
            $results += New-GateResult -Gate "tests.frontend.$($app.name)" -Level 'SHOULD' -Status 'skipped' `
                -Message 'no test_script configured'
            continue
        }
        $appPath = Join-Path $RepoRoot $app.path
        $pm = $app.package_manager
        if (-not $pm) { $pm = 'npm' }
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $r = Invoke-External -FilePath $pm -ArgumentList @('run', $app.test_script) -WorkingDirectory $appPath
        $sw.Stop()
        $stat = 'fail'
        if ($r.ExitCode -eq 0) { $stat = 'pass' }
        $details = $null
        if ($stat -eq 'fail') { $details = $r.StdOut + "`n" + $r.StdErr }
        $results += New-GateResult -Gate "tests.frontend.$($app.name)" -Level $level -Status $stat `
            -Message ("{0} in {1:N1}s" -f $app.test_script, $sw.Elapsed.TotalSeconds) `
            -Details $details
    }
}

return ,$results

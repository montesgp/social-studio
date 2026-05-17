# Build gate: dotnet restore+build for the configured solution, npm install+build for each frontend app.
# Returns: GateResult[]

[CmdletBinding()]
param(
    [Parameter(Mandatory)] $Config,
    [string] $RepoRoot,
    [string] $CoverageDir   # unused, accepted for orchestrator uniformity
)

. (Join-Path $PSScriptRoot '..\lib\Common.ps1')

$results = @()
$level = $Config.gates.build.level
if (-not $level) { $level = 'MUST' }

# ---- Backend ----
if ($Config.backend -and $Config.backend.enabled) {
    $solution = $Config.backend.solution
    $configuration = $Config.backend.build_configuration
    if (-not $configuration) { $configuration = 'Debug' }

    if (-not (Test-CommandAvailable 'dotnet')) {
        $results += New-GateResult -Gate 'build.backend' -Level $level -Status 'fail' `
            -Message 'dotnet SDK not found on PATH'
    } else {
        $solPath = Join-Path $RepoRoot $solution
        if (-not (Test-Path $solPath)) {
            $results += New-GateResult -Gate 'build.backend' -Level $level -Status 'fail' `
                -Message "Solution not found: $solution"
        } else {
            $sw = [System.Diagnostics.Stopwatch]::StartNew()
            $build = Invoke-External -FilePath 'dotnet' -ArgumentList @(
                'build', $solPath,
                '--configuration', $configuration,
                '--nologo',
                '-v','minimal'
            ) -WorkingDirectory $RepoRoot
            $sw.Stop()
            if ($build.ExitCode -eq 0) {
                $results += New-GateResult -Gate 'build.backend' -Level $level -Status 'pass' `
                    -Message ("solution built in {0:N1}s" -f $sw.Elapsed.TotalSeconds)
            } else {
                $results += New-GateResult -Gate 'build.backend' -Level $level -Status 'fail' `
                    -Message "dotnet build exited $($build.ExitCode)" `
                    -Details ($build.StdOut + "`n" + $build.StdErr)
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

        if (-not (Test-Path $appPath)) {
            $results += New-GateResult -Gate "build.frontend.$name" -Level $level -Status 'fail' `
                -Message "App path not found: $($app.path)"
            continue
        }
        if (-not (Test-CommandAvailable $pm)) {
            $results += New-GateResult -Gate "build.frontend.$name" -Level $level -Status 'fail' `
                -Message "$pm not found on PATH"
            continue
        }

        # Install
        $installArgs = $app.install_args
        if (-not $installArgs) { $installArgs = @('install','--no-audit','--no-fund') }
        $install = Invoke-External -FilePath $pm -ArgumentList $installArgs -WorkingDirectory $appPath
        if ($install.ExitCode -ne 0) {
            $results += New-GateResult -Gate "build.frontend.$name.install" -Level $level -Status 'fail' `
                -Message "$pm install exited $($install.ExitCode)" `
                -Details ($install.StdOut + "`n" + $install.StdErr)
            continue
        }

        # Build
        $buildScript = $app.build_script
        if (-not $buildScript) { $buildScript = 'build' }
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $build = Invoke-External -FilePath $pm -ArgumentList @('run', $buildScript) -WorkingDirectory $appPath
        $sw.Stop()
        if ($build.ExitCode -eq 0) {
            $results += New-GateResult -Gate "build.frontend.$name" -Level $level -Status 'pass' `
                -Message ("built in {0:N1}s" -f $sw.Elapsed.TotalSeconds)
        } else {
            $results += New-GateResult -Gate "build.frontend.$name" -Level $level -Status 'fail' `
                -Message "$pm run $buildScript exited $($build.ExitCode)" `
                -Details ($build.StdOut + "`n" + $build.StdErr)
        }

        # Lint (SHOULD)
        if ($app.lint_script) {
            $lint = Invoke-External -FilePath $pm -ArgumentList @('run', $app.lint_script) -WorkingDirectory $appPath
            if ($lint.ExitCode -eq 0) {
                $results += New-GateResult -Gate "build.frontend.$name.lint" -Level 'SHOULD' -Status 'pass' `
                    -Message 'lint clean'
            } else {
                $results += New-GateResult -Gate "build.frontend.$name.lint" -Level 'SHOULD' -Status 'warn' `
                    -Message "$pm run $($app.lint_script) exited $($lint.ExitCode)" `
                    -Details ($lint.StdOut + "`n" + $lint.StdErr)
            }
        }
    }
}

return ,$results

# Migration drift gate: fails/warns when the EF Core model no longer matches the last
# committed migration (issue #57 — DB migration delivery pipeline).
#
# Secret-free and DB-free by design:
#   - Primary check: `dotnet ef migrations has-pending-model-changes` (EF Core 8+, pinned
#     dotnet-ef 10.0.0 via dotnet-tools.json). This command builds the DbContext via the
#     design-time factory but never opens a real database connection — it only diffs the
#     current model against the migrations snapshot already committed to the repo.
#   - Fallback: scratch `dotnet ef migrations add __DriftCheck`, inspect whether the generated
#     Up() body is empty, then ALWAYS discard the scratch migration + restore the model
#     snapshot file, regardless of outcome.
#
# `AppIdentityDbContextFactory` still requires `ConnectionStrings:Postgres` to be a
# syntactically-valid connection string to construct the DbContext options — it is never used
# to open a connection for either code path above. A local placeholder value is injected only
# for the duration of the dotnet-ef invocations in this script and is not a real credential.

[CmdletBinding()]
param(
    [Parameter(Mandatory)] $Config,
    [string] $RepoRoot,
    [string] $CoverageDir   # unused, accepted for orchestrator uniformity
)

. (Join-Path $PSScriptRoot '..\lib\Common.ps1')

$results = @()
$gateCfg = $Config.gates.'migration-drift'
$level = if ($gateCfg -and $gateCfg.level) { $gateCfg.level } else { 'SHOULD' }

if (-not (Test-CommandAvailable 'dotnet')) {
    $results += New-GateResult -Gate 'migration-drift' -Level $level -Status 'skipped' `
        -Message 'dotnet SDK not available'
    return ,$results
}

$infrastructureProject = Join-Path $RepoRoot 'Incoders.Hive.Infrastructure'
$apiServiceProject = Join-Path $RepoRoot 'Incoders.Hive.ApiService'
$migrationsDir = Join-Path $infrastructureProject 'Persistence\Postgres\Migrations'
$snapshotPath = Join-Path $migrationsDir 'AppIdentityDbContextModelSnapshot.cs'

if (-not (Test-Path $infrastructureProject) -or -not (Test-Path $apiServiceProject)) {
    $results += New-GateResult -Gate 'migration-drift' -Level $level -Status 'skipped' `
        -Message 'Infrastructure/ApiService project not found'
    return ,$results
}

$buildConfiguration = if ($Config.backend -and $Config.backend.build_configuration) { $Config.backend.build_configuration } else { 'Release' }
# Force the configured (Release-by-default) build configuration. Debug builds on Windows also
# trigger the local-dev binary-signing MSBuild target (`LocalDevSignBinaries`, ApiService/AppHost
# csproj), which is unrelated to drift detection and can fail under local security policies that
# block ad-hoc script signing. Release avoids that target entirely and matches CI.
$efProjectArgs = @('--project', $infrastructureProject, '--startup-project', $apiServiceProject, '--configuration', $buildConfiguration)

# Placeholder connection string. Not a real credential, never dialed - the commands below only
# diff source code against the committed model snapshot.
$driftPlaceholderConnection = 'Host=localhost;Port=5432;Database=migration_drift_probe;Username=drift_check;Password=drift_check'

function Invoke-DotnetEf {
    param([string[]] $Arguments, [string] $WorkingDirectory)
    $prevValue = $env:ConnectionStrings__Postgres
    $env:ConnectionStrings__Postgres = $driftPlaceholderConnection
    try {
        return Invoke-External -FilePath 'dotnet' -ArgumentList (@('ef') + $Arguments) -WorkingDirectory $WorkingDirectory
    } finally {
        if ($null -eq $prevValue) {
            Remove-Item Env:\ConnectionStrings__Postgres -ErrorAction SilentlyContinue
        } else {
            $env:ConnectionStrings__Postgres = $prevValue
        }
    }
}

# ---- Ensure the pinned dotnet-ef tool is available ----
$restore = Invoke-External -FilePath 'dotnet' -ArgumentList @('tool', 'restore') -WorkingDirectory $RepoRoot
if ($restore.ExitCode -ne 0) {
    $results += New-GateResult -Gate 'migration-drift' -Level $level -Status 'fail' `
        -Message 'dotnet tool restore failed' -Details ($restore.StdOut)
    return ,$results
}

# ---- Primary: has-pending-model-changes ----
$primaryArgs = @('migrations', 'has-pending-model-changes') + $efProjectArgs
$primary = Invoke-DotnetEf -Arguments $primaryArgs -WorkingDirectory $RepoRoot

$verbUnsupported = $primary.StdOut -match '(?i)(unrecognized command or argument|no such command|is not a dotnet command|unknown command|unknown option)'
$buildFailed = $primary.StdOut -match '(?i)Build failed\.'

if ($buildFailed) {
    # The underlying `dotnet build` that dotnet-ef performs before diffing the model failed for
    # a reason unrelated to schema drift (compile error, local toolchain issue, etc.). Report it
    # distinctly instead of mislabeling a build failure as "pending model changes".
    $results += New-GateResult -Gate 'migration-drift' -Level $level -Status 'fail' `
        -Message 'could not evaluate drift: dotnet-ef build failed before comparing the model' `
        -Details $primary.StdOut
    return ,$results
}

if (-not $verbUnsupported) {
    if ($primary.ExitCode -eq 0) {
        $results += New-GateResult -Gate 'migration-drift' -Level $level -Status 'pass' `
            -Message 'EF model matches committed migrations (has-pending-model-changes)'
    } else {
        $status = if ($level -eq 'MUST') { 'fail' } else { 'warn' }
        $results += New-GateResult -Gate 'migration-drift' -Level $level -Status $status `
            -Message 'EF model has pending changes not captured by a migration' -Details $primary.StdOut
    }
    return ,$results
}

# ---- Fallback: scratch-add-then-diff ----
# The primary verb is unavailable/misbehaving on this dotnet-ef version. Generate a throwaway
# migration and inspect whether Up() is empty (no drift) or not (drift). Always clean up.
$scratchName = '__DriftCheck'
$scratchMainFile = $null
$scratchDesignerFile = $null
$snapshotBackup = if (Test-Path $snapshotPath) { Get-Content -LiteralPath $snapshotPath -Raw } else { $null }
$existingBefore = @(Get-ChildItem -Path $migrationsDir -Filter '*.cs' -File -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name)

try {
    $addArgs = @('migrations', 'add', $scratchName) + $efProjectArgs
    $add = Invoke-DotnetEf -Arguments $addArgs -WorkingDirectory $RepoRoot

    $newFiles = @(Get-ChildItem -Path $migrationsDir -Filter "*_$scratchName.cs" -File -ErrorAction SilentlyContinue)
    $newDesignerFiles = @(Get-ChildItem -Path $migrationsDir -Filter "*_$scratchName.Designer.cs" -File -ErrorAction SilentlyContinue)
    if ($newFiles.Count -gt 0) { $scratchMainFile = $newFiles[0].FullName }
    if ($newDesignerFiles.Count -gt 0) { $scratchDesignerFile = $newDesignerFiles[0].FullName }

    if ($add.ExitCode -ne 0 -or -not $scratchMainFile) {
        $results += New-GateResult -Gate 'migration-drift' -Level $level -Status 'fail' `
            -Message 'fallback scratch-add failed (has-pending-model-changes unavailable)' `
            -Details $add.StdOut
        return ,$results
    }

    $content = Get-Content -LiteralPath $scratchMainFile -Raw
    $upMatch = [regex]::Match(
        $content,
        '(?s)protected override void Up\(MigrationBuilder migrationBuilder\)\s*\{(?<body>.*?)\}\s*\r?\n\s*(?:/// <inheritdoc />\s*\r?\n\s*)?protected override void Down'
    )
    $upBody = if ($upMatch.Success) { $upMatch.Groups['body'].Value.Trim() } else { $content }
    $hasDrift = -not [string]::IsNullOrWhiteSpace($upBody)

    if ($hasDrift) {
        $status = if ($level -eq 'MUST') { 'fail' } else { 'warn' }
        $results += New-GateResult -Gate 'migration-drift' -Level $level -Status $status `
            -Message 'EF model has pending changes not captured by a migration (scratch-add fallback)' `
            -Details $upBody
    } else {
        $results += New-GateResult -Gate 'migration-drift' -Level $level -Status 'pass' `
            -Message 'EF model matches committed migrations (scratch-add fallback)'
    }
} finally {
    # Always discard the scratch migration, regardless of outcome.
    if ($scratchMainFile -and (Test-Path $scratchMainFile)) { Remove-Item -LiteralPath $scratchMainFile -Force -ErrorAction SilentlyContinue }
    if ($scratchDesignerFile -and (Test-Path $scratchDesignerFile)) { Remove-Item -LiteralPath $scratchDesignerFile -Force -ErrorAction SilentlyContinue }
    # Remove any other stray file matching the scratch name that wasn't captured above.
    Get-ChildItem -Path $migrationsDir -Filter "*_$scratchName*.cs" -File -ErrorAction SilentlyContinue |
        Where-Object { $existingBefore -notcontains $_.Name } |
        ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue }
    # Restore the shared model snapshot file to its committed state.
    if ($null -ne $snapshotBackup) {
        Set-Content -LiteralPath $snapshotPath -Value $snapshotBackup -NoNewline
    }
}

return ,$results

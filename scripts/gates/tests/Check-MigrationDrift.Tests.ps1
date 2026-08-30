$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\lib\MigrationDrift.ps1')

function Assert-Equal($Expected, $Actual, [string] $Message) {
    if ($Expected -ne $Actual) {
        throw "$Message. Expected '$Expected', got '$Actual'."
    }
}

# ---- no-drift: exit 0 is EF's only success signal ----
Assert-Equal 'no-drift' `
    (Get-MigrationDriftClassification -ExitCode 0 -StdOut 'No changes have been made to the model since the last migration.') `
    'Exit 0 with the clean message must classify as no-drift'

Assert-Equal 'no-drift' `
    (Get-MigrationDriftClassification -ExitCode 0 -StdOut '') `
    'Exit 0 must classify as no-drift even with empty stdout'

# ---- drift: the real EF "pending model changes" signal, captured verbatim from dotnet-ef 10.0.0 ----
$realDriftStdOut = @'
Build started...
Build succeeded.
Changes have been made to the model since the last migration. Add a new migration.
'@
Assert-Equal 'drift' `
    (Get-MigrationDriftClassification -ExitCode 1 -StdOut $realDriftStdOut) `
    'The confirmed EF pending-changes phrase at exit 1 must classify as drift'

# ---- tooling-error: a build failure must NEVER be reported as drift ----
$buildFailedStdOut = @'
C:\repo\Incoders.Hive.ApiService\Incoders.Hive.ApiService.csproj(79,5): error MSB3073: The command "powershell -NoProfile -ExecutionPolicy Bypass -File Sign-LocalDevBinaries.ps1" exited with code 1.
Build FAILED.
Microsoft.EntityFrameworkCore.Tools.CommandException: Build failed. Use dotnet build to see the errors.
'@
Assert-Equal 'tooling-error' `
    (Get-MigrationDriftClassification -ExitCode 1 -StdOut $buildFailedStdOut) `
    'A dotnet-ef build failure must classify as tooling-error, not drift'

# ---- tooling-error: AppIdentityDbContextFactory throwing must NEVER be reported as drift ----
$factoryExceptionStdOut = @'
Build succeeded.
Unable to create a 'DbContext' of type 'AppIdentityDbContext'. The exception 'Connection string 'Postgres' must be configured in user-secrets or environment variables.' was thrown while attempting to create an instance. For the different patterns supported at design time, see https://go.microsoft.com/fwlink/?linkid=851728
'@
Assert-Equal 'tooling-error' `
    (Get-MigrationDriftClassification -ExitCode 1 -StdOut $factoryExceptionStdOut) `
    'A design-time factory exception must classify as tooling-error, not drift'

# ---- tooling-error: any other unrelated non-zero exit must NEVER be reported as drift ----
Assert-Equal 'tooling-error' `
    (Get-MigrationDriftClassification -ExitCode 1 -StdOut 'System.IO.IOException: The process cannot access the file because it is being used by another process.') `
    'An arbitrary unrelated crash must classify as tooling-error, not drift'

Assert-Equal 'tooling-error' `
    (Get-MigrationDriftClassification -ExitCode 1 -StdOut '') `
    'A non-zero exit with no recognizable signal at all must classify as tooling-error, not drift'

# ---- verb-unsupported: fallback trigger, checked ahead of exit-code/message inspection ----
Assert-Equal 'verb-unsupported' `
    (Get-MigrationDriftClassification -ExitCode 1 -StdOut "Unrecognized command or argument 'has-pending-model-changes'.") `
    'An unrecognized-verb message must classify as verb-unsupported so the fallback runs'

Assert-Equal 'verb-unsupported' `
    (Get-MigrationDriftClassification -ExitCode 0 -StdOut "Unknown option '--configuration'.") `
    'verb-unsupported detection takes priority even over an exit-0 result'

Write-Host 'Check-MigrationDrift classification tests passed.'

# Pure classification helper for the migration-drift gate (issue #57).
#
# Kept separate from Check-MigrationDrift.ps1 (which shells out to dotnet-ef and has file-system
# side effects) so the classification logic itself is unit-testable with canned exit
# codes/stdout, no dotnet/DB required. See scripts/gates/tests/Check-MigrationDrift.Tests.ps1.

# Classifies the outcome of `dotnet ef migrations has-pending-model-changes` from its exit code
# and captured stdout. Returns exactly one of:
#   'no-drift'         - exit 0. EF's only success signal; always means the model matches.
#   'drift'            - EF's real "pending model changes" phrase was found in stdout.
#   'verb-unsupported' - the dotnet-ef CLI does not recognize the verb/option on this version.
#   'tooling-error'    - any other non-zero exit (build failure, DbContext construction
#                        exception, unrelated crash, etc.) - NOT evidence of schema drift.
#
# IMPORTANT: classification is positive-detection for 'drift', not "anything non-zero is
# drift". A build failure or a design-time factory exception is a tooling/config problem, not a
# statement about the EF model, and must never be reported as drift.
function Get-MigrationDriftClassification {
    param(
        [Parameter(Mandatory)] [int] $ExitCode,
        [AllowEmptyString()] [string] $StdOut = ''
    )

    if ($StdOut -match '(?i)(unrecognized command or argument|no such command|is not a dotnet command|unknown command|unknown option)') {
        return 'verb-unsupported'
    }

    if ($ExitCode -eq 0) {
        return 'no-drift'
    }

    # Confirmed against dotnet-ef 10.0.0: the pending-changes case prints this exact phrase to
    # stdout and exits 1. Any other non-zero exit (build failure, factory exception, etc.) is an
    # inconclusive tooling error, not drift.
    if ($StdOut -match '(?i)Changes have been made to the model since the last migration') {
        return 'drift'
    }

    return 'tooling-error'
}

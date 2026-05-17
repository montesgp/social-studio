# Shared helpers for quality gates scripts.
# Pure pwsh, no external module dependency.

# Note: StrictMode intentionally disabled - hashtable/XML/PSObject property access
# in PS 5.1 strict mode throws on missing properties, which breaks defensive checks.
$ErrorActionPreference = 'Stop'

# ----- Result model -----
class GateResult {
    [string] $Gate
    [string] $Level   # MUST | SHOULD | MAY
    [string] $Status  # pass | warn | fail | skipped
    [string] $Message
    [object] $Details
}

function New-GateResult {
    param(
        [Parameter(Mandatory)] [string] $Gate,
        [Parameter(Mandatory)] [ValidateSet('MUST','SHOULD','MAY')] [string] $Level,
        [Parameter(Mandatory)] [ValidateSet('pass','warn','fail','skipped')] [string] $Status,
        [Parameter(Mandatory)] [string] $Message,
        [object] $Details = $null
    )
    $r = [GateResult]::new()
    $r.Gate    = $Gate
    $r.Level   = $Level
    $r.Status  = $Status
    $r.Message = $Message
    $r.Details = $Details
    return $r
}

function Format-GateLine {
    param([Parameter(Mandatory)] [GateResult] $Result)
    return ('[ {0,-6} ] {1}: {2} - {3}' -f $Result.Level, $Result.Gate, $Result.Status, $Result.Message)
}

function Write-GateResult {
    param([Parameter(Mandatory)] [GateResult] $Result)
    $color = switch ($Result.Status) {
        'pass'    { 'Green' }
        'warn'    { 'Yellow' }
        'fail'    { 'Red' }
        'skipped' { 'DarkGray' }
        default   { 'White' }
    }
    Write-Host (Format-GateLine -Result $Result) -ForegroundColor $color
}

function Get-RepoRoot {
    # Resolve repo root: this file lives at scripts/gates/lib/Common.ps1
    $here = Split-Path -Parent $PSCommandPath
    return (Resolve-Path (Join-Path $here '..\..\..')).Path
}

function Test-CommandAvailable {
    param([Parameter(Mandatory)] [string] $Command)
    return [bool] (Get-Command $Command -ErrorAction SilentlyContinue)
}

function Invoke-External {
    <#
        Runs an external command capturing stdout+stderr+exit code without throwing.
        Works on Windows PowerShell 5.1 and PowerShell 7+. Stderr is merged into
        StdOut (via 2>&1); StdErr is kept empty for shape compatibility.
        Returns: @{ ExitCode; StdOut; StdErr; Duration }
    #>
    param(
        [Parameter(Mandatory)] [string] $FilePath,
        [string[]] $ArgumentList = @(),
        [string] $WorkingDirectory = (Get-Location).Path
    )
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $prevLocation = (Get-Location).Path
    # PS 5.1 wraps native stderr lines in ErrorRecord (NativeCommandError); with
    # ErrorActionPreference=Stop that throws even on exit-0. Force Continue locally.
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $output = $null
    $exitCode = 0
    try {
        Set-Location -LiteralPath $WorkingDirectory
        $output = & $FilePath @ArgumentList 2>&1 | Out-String
        $exitCode = $LASTEXITCODE
        if ($null -eq $exitCode) { $exitCode = 0 }
    } catch {
        $output = "$output`nException: $($_.Exception.Message)"
        $exitCode = -1
    } finally {
        Set-Location -LiteralPath $prevLocation
        $ErrorActionPreference = $prevEap
        $sw.Stop()
    }
    return @{
        ExitCode = $exitCode
        StdOut   = if ($null -ne $output) { [string] $output } else { '' }
        StdErr   = ''
        Duration = $sw.Elapsed
    }
}

# Convert a wildcard-style namespace prefix ("Microsoft.EntityFrameworkCore" or "Microsoft.AspNetCore.*")
# into a regex anchored at the start of a `using` line.
function ConvertTo-NamespaceRegex {
    param([Parameter(Mandatory)] [string] $Pattern)
    $p = $Pattern.TrimEnd('*').TrimEnd('.')
    $escaped = [regex]::Escape($p)
    return "^\s*(global\s+)?using(\s+static)?\s+$escaped(\b|\.)"
}

# Aggregate exit code: 1 if any MUST failed, else 0.
function Get-AggregateExitCode {
    param([Parameter(Mandatory)] [GateResult[]] $Results)
    $hasMustFail = $Results | Where-Object { $_.Level -eq 'MUST' -and $_.Status -eq 'fail' }
    if ($hasMustFail) { return 1 } else { return 0 }
}

function Write-GateSummary {
    param([Parameter(Mandatory)] [GateResult[]] $Results)
    Write-Host ''
    Write-Host '=== Gate Summary ===' -ForegroundColor Cyan
    $byStatus = $Results | Group-Object Status | Sort-Object Name
    foreach ($g in $byStatus) {
        Write-Host ('  {0,-8} {1}' -f $g.Name, $g.Count)
    }
    $mustFail = ($Results | Where-Object { $_.Level -eq 'MUST' -and $_.Status -eq 'fail' }).Count
    Write-Host ''
    if ($mustFail -gt 0) {
        Write-Host "FAIL - $mustFail MUST gate(s) failed." -ForegroundColor Red
    } else {
        Write-Host 'PASS - no MUST gate failed.' -ForegroundColor Green
    }
}

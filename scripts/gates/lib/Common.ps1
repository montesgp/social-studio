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
    $safeMessage = Protect-GateOutput -Text $Result.Message
    return ('[ {0,-6} ] {1}: {2} - {3}' -f $Result.Level, $Result.Gate, $Result.Status, $safeMessage)
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
    if ($Result.Details -and ($Result.Status -eq 'fail' -or $Result.Status -eq 'warn')) {
        Write-Host (Format-GateDetails -Details $Result.Details) -ForegroundColor DarkGray
    }
}

function Format-GateDetails {
    param(
        [Parameter(Mandatory)] $Details,
        [ValidateRange(1, 2147483647)] [int] $MaxLines = 80,
        [ValidateRange(1, 2147483647)] [int] $MaxCharacters = 6000
    )
    $text = Protect-GateOutput -Text ([string] $Details)
    $lines = @($text -split "`r?`n")
    $truncated = $lines.Count -gt $MaxLines
    if ($truncated) {
        if ($MaxLines -le 1) {
            $lines = @('...')
        } elseif ($MaxLines -eq 2) {
            $lines = @($lines[0], $lines[-1])
        } else {
            $headCount = [Math]::Floor(($MaxLines - 1) / 2)
            $tailCount = $MaxLines - 1 - $headCount
            $lines = @($lines[0..($headCount - 1)]) + @('... details truncated; tail follows ...') + @($lines[($lines.Count - $tailCount)..($lines.Count - 1)])
        }
    }
    $formatted = ($lines | ForEach-Object { "    $_" }) -join "`n"
    if ($formatted.Length -gt $MaxCharacters) {
        $marker = '...[truncated]...'
        if ($MaxCharacters -le $marker.Length) {
            $formatted = $marker.Substring(0, [Math]::Max(0, $MaxCharacters))
        } else {
            $available = $MaxCharacters - $marker.Length
            $headChars = [Math]::Floor($available / 2)
            $tailChars = $available - $headChars
            $formatted = $formatted.Substring(0, $headChars) + $marker + $formatted.Substring($formatted.Length - $tailChars)
        }
        $truncated = $true
    }
    return $formatted
}

function Protect-GateOutput {
    param([AllowEmptyString()] [string] $Text)
    if ([string]::IsNullOrEmpty($Text)) { return $Text }

    # Remove complete private-key blocks before line-oriented sanitization.
    $sanitized = [regex]::Replace(
        $Text,
        '(?is)-----BEGIN (?:RSA |EC |DSA |OPENSSH |PGP )?PRIVATE KEY-----.*?-----END (?:RSA |EC |DSA |OPENSSH |PGP )?PRIVATE KEY-----',
        '[REDACTED_PRIVATE_KEY]')

    # Known standalone credential formats enforced by the MUST secrets gate.
    $knownPatterns = @(
        '(?i)\bAKIA[0-9A-Z]{16}\b',
        '(?i)\beyJ[A-Za-z0-9_\-]{10,}\.eyJ[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}\b',
        '(?i)\bsbp_[A-Za-z0-9]{40,}\b',
        '(?i)\bsb-[A-Za-z0-9-]{20,}\b',
        '(?i)\bghp_[A-Za-z0-9]{36,}\b',
        '(?i)\bgithub_pat_[A-Za-z0-9_]{82,}\b',
        '(?i)\bsk-ant-[A-Za-z0-9_\-]{32,}\b',
        '(?i)\bsk-[A-Za-z0-9]{20,}\b'
    )
    foreach ($pattern in $knownPatterns) {
        $sanitized = [regex]::Replace($sanitized, $pattern, '[REDACTED_CREDENTIAL]')
    }

    # Redact quoted assignments as a whole, including whitespace and embedded newlines.
    $keyName = '(?:password|passwd|pwd|secret|token|api[_-]?key|access[_-]?key|secret[_-]?access[_-]?key|aws[_-]?secret[_-]?access[_-]?key|connection[_-]?string|credential|authorization|client[_-]?secret|private[_-]?key)'
    $quotedAssignment = ('(?is)((?<![A-Za-z0-9]){0}(?![A-Za-z0-9])\s*[=:]\s*)([''"])(.*?)\2' -f $keyName)
    $sanitized = [regex]::Replace($sanitized, $quotedAssignment, {
        param($match)
        return $match.Groups[1].Value + '[REDACTED]'
    })

    # Unquoted assignments stop at a line or common connection-string delimiter.
    $unquotedAssignment = ('(?im)((?<![A-Za-z0-9]){0}(?![A-Za-z0-9])\s*[=:]\s*)(?!\[REDACTED\])[^\r\n;,]+' -f $keyName)
    $sanitized = [regex]::Replace($sanitized, $unquotedAssignment, {
        param($match)
        return $match.Groups[1].Value + '[REDACTED]'
    })

    # User-info embedded in URLs is credential material even without a key name.
    $sanitized = [regex]::Replace(
        $sanitized,
        '(?i)(https?://)[^\s/@:]+:[^\s/@]+@',
        '$1[REDACTED_CREDENTIALS]@')

    return $sanitized
}

function ConvertTo-SafeGateResult {
    param([Parameter(Mandatory)] [GateResult] $Result)
    return [pscustomobject]@{
        Gate = $Result.Gate
        Level = $Result.Level
        Status = $Result.Status
        Message = Protect-GateOutput -Text $Result.Message
        Details = if ($null -eq $Result.Details) { $null } else { Protect-GateOutput -Text ([string] $Result.Details) }
    }
}

function ConvertTo-SafeGateResultsJson {
    param([Parameter(Mandatory)] [GateResult[]] $Results)
    $safeResults = @($Results | ForEach-Object { ConvertTo-SafeGateResult -Result $_ })
    return ConvertTo-Json -InputObject $safeResults -Depth 6
}

function Get-ProjectReferenceName {
    param([Parameter(Mandatory)] [string] $Include)
    $normalized = $Include.Replace('\', '/')
    $leaf = ($normalized -split '/')[-1]
    return [System.IO.Path]::GetFileNameWithoutExtension($leaf)
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

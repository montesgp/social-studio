# Secrets gate: regex scan over tracked text files for known credential patterns.
# Phase 1: pure pwsh implementation, no external tool. Phase 2 may swap to gitleaks.

[CmdletBinding()]
param(
    [Parameter(Mandatory)] $Config,
    [string] $RepoRoot,
    [string] $CoverageDir   # unused, accepted for orchestrator uniformity
)

. (Join-Path $PSScriptRoot '..\lib\Common.ps1')

$results = @()
$level = $Config.gates.secrets.level
if (-not $level) { $level = 'MUST' }

$secretsCfg = $Config.secrets
$ignorePaths = @()
if ($secretsCfg -and $secretsCfg.ignore_paths) {
    $ignorePaths = @($secretsCfg.ignore_paths)
}
$scanPaths = @('.')
if ($secretsCfg -and $secretsCfg.scan_paths) { $scanPaths = @($secretsCfg.scan_paths) }
$extraPatterns = @()
if ($secretsCfg -and $secretsCfg.patterns_extra) { $extraPatterns = @($secretsCfg.patterns_extra) }

# Built-in patterns
$patterns = @(
    @{ Name='aws-access-key-id';   Regex='AKIA[0-9A-Z]{16}' },
    @{ Name='aws-secret-access-key'; Regex='(?i)aws.{0,20}?(secret|access).{0,20}?[=:]\s*["'']?[A-Za-z0-9/+=]{40}["'']?' },
    @{ Name='private-key-pem';     Regex='-----BEGIN (RSA |EC |DSA |OPENSSH |PGP )?PRIVATE KEY-----' },
    @{ Name='jwt-token';           Regex='eyJ[A-Za-z0-9_\-]{10,}\.eyJ[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}' },
    @{ Name='supabase-service-key'; Regex='sbp_[A-Za-z0-9]{40,}' },
    @{ Name='github-pat';          Regex='ghp_[A-Za-z0-9]{36,}' },
    @{ Name='github-fine-pat';     Regex='github_pat_[A-Za-z0-9_]{82,}' },
    @{ Name='openai-api-key';      Regex='sk-(?!ant-)[A-Za-z0-9]{20,}' },
    @{ Name='anthropic-api-key';   Regex='sk-ant-[A-Za-z0-9_\-]{32,}' },
    @{ Name='generic-secret-assignment'; Regex='(?i)(api[_-]?key|secret|token|password|passwd)\s*[:=]\s*["'']([^"''\s]{16,})["'']' }
)
foreach ($p in $extraPatterns) { $patterns += $p }

# Placeholder detector - values that don't count as secrets
$placeholderRegex = '(?i)^(xxx+|your[_\-].*|change[_\-]?me|<.*>|placeholder|sample|example|todo|fixme|n/?a|null|none|secret[_\-]?here|.*[_\-]here|paste[_\-].*|\$\{.*\}|\${env:.+\}|#\{.+\}|from-user-secrets|__.*__|YOUR[_-].*|unit[_\-]test[_\-].*|test[_\-]secret.*|fake[_\-].*|dummy[_\-].*|mock[_\-].*|seed[_\-].*)$'

$findings = @()
$checked = 0

# Two ignore modes:
#   - entries with a path separator -> match as absolute prefix (relative to repo)
#   - entries without a separator -> match as directory name at any depth
$ignoreAbsPrefixes = @()
$ignoreDirNames = @()
foreach ($ip in $ignorePaths) {
    if ($ip -match '[\\/]') {
        $abs = Join-Path $RepoRoot $ip
        if (Test-Path -LiteralPath $abs) {
            $ignoreAbsPrefixes += (Resolve-Path -LiteralPath $abs).Path
        }
    } else {
        $ignoreDirNames += $ip
    }
}
# Always exclude common build/cache directory names regardless of config
$alwaysSkipDirs = @('bin','obj','node_modules','.git','.vs','.next','.alt','dist','out','target','.gradle','.idea','.cache','TestResults','coverage')
foreach ($d in $alwaysSkipDirs) { if ($ignoreDirNames -notcontains $d) { $ignoreDirNames += $d } }

foreach ($scanRoot in $scanPaths) {
    $rootFull = Join-Path $RepoRoot $scanRoot
    if (-not (Test-Path $rootFull)) { continue }
    $files = Get-ChildItem -Path $rootFull -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object {
            $full = $_.FullName
            # Absolute prefix exclusions
            foreach ($ip in $ignoreAbsPrefixes) {
                if ($full.StartsWith($ip, [System.StringComparison]::OrdinalIgnoreCase)) { return $false }
            }
            # Directory-name exclusions at any depth
            foreach ($dn in $ignoreDirNames) {
                if ($full -match "[\\/]" + [regex]::Escape($dn) + "[\\/]") { return $false }
            }
            if ($_.Length -gt 1MB) { return $false }
            $ext = $_.Extension.ToLowerInvariant()
            $binExts = @('.png','.jpg','.jpeg','.gif','.webp','.ico','.pdf','.zip','.gz','.tar','.7z','.dll','.exe','.so','.dylib','.pdb','.cache','.lock','.lscache','.nupkg','.snupkg','.bin','.woff','.woff2','.ttf','.eot','.mp3','.mp4','.mov')
            if ($binExts -contains $ext) { return $false }
            return $true
        }

    foreach ($file in $files) {
        $checked++
        try {
            $lines = Get-Content -LiteralPath $file.FullName -ErrorAction Stop
        } catch { continue }
        $lineNo = 0
        foreach ($line in $lines) {
            $lineNo++
            # Per-line opt-out: '// gates:secret-allow' or '# gates:secret-allow'
            if ($line -match 'gates:secret-allow') { continue }
            foreach ($p in $patterns) {
                $m = [regex]::Match($line, $p.Regex)
                if ($m.Success) {
                    # Filter generic-secret-assignment placeholders
                    if ($p.Name -eq 'generic-secret-assignment') {
                        $val = $m.Groups[2].Value
                        if ($val -match $placeholderRegex) { continue }
                        if ($val.Length -lt 16) { continue }
                    }
                    $snippet = $m.Value
                    if ($snippet.Length -gt 12) {
                        $snippet = $snippet.Substring(0,4) + '...' + $snippet.Substring($snippet.Length - 4, 4)
                    } else {
                        $snippet = '***'
                    }
                    $findings += [pscustomobject]@{
                        File   = $file.FullName.Replace($RepoRoot, '').TrimStart('\','/')
                        Line   = $lineNo
                        Pattern= $p.Name
                        Snippet= $snippet
                    }
                }
            }
        }
    }
}

if ($findings.Count -eq 0) {
    $results += New-GateResult -Gate 'secrets.scan' -Level $level -Status 'pass' `
        -Message ("clean ({0} files scanned)" -f $checked)
} else {
    $details = ($findings | ForEach-Object { "$($_.File):$($_.Line): $($_.Pattern): $($_.Snippet)" }) -join "`n"
    $results += New-GateResult -Gate 'secrets.scan' -Level $level -Status 'fail' `
        -Message ("{0} finding(s) in {1} files scanned" -f $findings.Count, $checked) -Details $details
}

# File hygiene: sensitive files MUST be gitignored
$sensitive = @('.mcp.json', '.env', '.env.local', 'appsettings.Local.json', 'secrets.json')
$giPath = Join-Path $RepoRoot '.gitignore'
$hygieneIssues = @()
if (Test-Path $giPath) {
    $gi = Get-Content -LiteralPath $giPath -Raw
    foreach ($s in $sensitive) {
        $base = Split-Path -Leaf $s
        if ($gi -notmatch [regex]::Escape($base)) {
            $hygieneIssues += "$s missing from .gitignore"
        }
    }
} else {
    $hygieneIssues += '.gitignore missing'
}
if ($hygieneIssues.Count -eq 0) {
    $results += New-GateResult -Gate 'secrets.hygiene' -Level $level -Status 'pass' `
        -Message 'sensitive files gitignored'
} else {
    $results += New-GateResult -Gate 'secrets.hygiene' -Level $level -Status 'fail' `
        -Message ('{0} hygiene issue(s)' -f $hygieneIssues.Count) -Details ($hygieneIssues -join "`n")
}

return ,$results

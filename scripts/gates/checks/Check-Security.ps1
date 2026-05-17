# Security baseline gate:
#   - configuration hygiene (no real secrets in tracked appsettings*.json)
#   - insecure defaults (cert callback bypass, AllowAnyOrigin+AllowCredentials)
#   - JWT min length warning
#   - non-https URLs in non-Development settings (SHOULD)

[CmdletBinding()]
param(
    [Parameter(Mandatory)] $Config,
    [string] $RepoRoot,
    [string] $CoverageDir   # unused, accepted for orchestrator uniformity
)

. (Join-Path $PSScriptRoot '..\lib\Common.ps1')

$results = @()
$level = $Config.gates.security.level
if (-not $level) { $level = 'MUST' }
$jwtMin = 32
if ($Config.security -and $Config.security.jwt_min_length) { $jwtMin = [int] $Config.security.jwt_min_length }

# ---- Configuration hygiene ----
$placeholderRegex = '(?i)^(\s*|null|<.*>|change[\-_]?me|placeholder|sample|example|n/?a|\$\{.*\}|from-user-secrets|.*-here|__.*__|YOUR[_-].*|\*+|x{3,})\s*$'
$sensitiveKeyRegex = '(?i)(secret|password|connectionstring|apikey|api[_-]?key)$'
$configFindings = @()

$appsettings = Get-ChildItem -Path $RepoRoot -Filter 'appsettings*.json' -File -Recurse -ErrorAction SilentlyContinue |
    Where-Object {
        ($_.FullName -notmatch '[\\/](bin|obj|node_modules|\.next|\.vs)[\\/]') -and
        ($_.Name -notlike '*.example*') -and
        ($_.Name -notlike '*.template*')
    }

function Get-JsonStringPaths($node, $pathStack) {
    if ($null -eq $node) { return }
    $isDict = $false
    try {
        if ($node -is [System.Collections.IDictionary]) { $isDict = $true }
        elseif ($node.PSObject.TypeNames -contains 'System.Management.Automation.PSCustomObject') { $isDict = $true }
    } catch { }
    if (-not $isDict) { return }
    foreach ($prop in $node.PSObject.Properties) {
        $key = $prop.Name
        $val = $prop.Value
        $newPath = $pathStack + $key
        if ($val -is [string]) {
            [pscustomobject]@{ Path = ($newPath -join ':'); Value = $val }
        } else {
            Get-JsonStringPaths $val $newPath
        }
    }
}

foreach ($f in $appsettings) {
    try {
        $obj = Get-Content -LiteralPath $f.FullName -Raw | ConvertFrom-Json -ErrorAction Stop
    } catch { continue }
    $pairs = @(Get-JsonStringPaths $obj @())
    foreach ($pair in $pairs) {
        $keyPath = $pair.Path
        $val = $pair.Value
        $leaf = ($keyPath -split ':')[-1]
        if ($leaf -match $sensitiveKeyRegex) {
            if ($val -and $val -notmatch $placeholderRegex) {
                $configFindings += [pscustomobject]@{
                    File = $f.FullName.Replace($RepoRoot, '').TrimStart('\','/')
                    Key  = $keyPath
                    Why  = 'sensitive-looking value not a placeholder'
                }
            }
        }
        if ($leaf -ieq 'Secret' -and $keyPath -match '(?i)Jwt') {
            if ($val -and $val.Length -lt $jwtMin -and $val -notmatch $placeholderRegex) {
                $configFindings += [pscustomobject]@{
                    File = $f.FullName.Replace($RepoRoot, '').TrimStart('\','/')
                    Key  = $keyPath
                    Why  = "JWT secret length $($val.Length) below min $jwtMin"
                }
            }
        }
    }
}

if ($configFindings.Count -eq 0) {
    $results += New-GateResult -Gate 'security.config' -Level $level -Status 'pass' `
        -Message ('{0} appsettings file(s) clean' -f $appsettings.Count)
} else {
    $details = ($configFindings | ForEach-Object { "$($_.File): $($_.Key) - $($_.Why)" }) -join "`n"
    $results += New-GateResult -Gate 'security.config' -Level $level -Status 'fail' `
        -Message ('{0} finding(s)' -f $configFindings.Count) -Details $details
}

# ---- Insecure defaults in code (MUST) ----
$insecurePatterns = @(
    @{ Name='cert-validation-bypass'; Regex='ServerCertificateCustomValidationCallback\s*=\s*\([^)]*\)\s*=>\s*true' },
    @{ Name='cors-any-origin-with-credentials'; Regex='AllowAnyOrigin\s*\(\s*\)[\s\S]{0,200}AllowCredentials\s*\(\s*\)' }
)
$insecureFindings = @()
$arch = $Config.backend.architecture
$scanProjects = @()
if ($arch.api_projects) { $scanProjects += $arch.api_projects }
if ($arch.infrastructure_projects) { $scanProjects += $arch.infrastructure_projects }

foreach ($proj in $scanProjects) {
    $projDir = Join-Path $RepoRoot $proj
    if (-not (Test-Path $projDir)) { continue }
    $cs = Get-ChildItem -Path $projDir -Filter '*.cs' -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
    foreach ($file in $cs) {
        $text = Get-Content -LiteralPath $file.FullName -Raw
        foreach ($p in $insecurePatterns) {
            $m = [regex]::Matches($text, $p.Regex)
            foreach ($mm in $m) {
                # find approx line number
                $line = ($text.Substring(0, $mm.Index) -split "`n").Count
                $insecureFindings += [pscustomobject]@{
                    File = $file.FullName.Replace($RepoRoot, '').TrimStart('\','/')
                    Line = $line
                    Pattern = $p.Name
                }
            }
        }
    }
}
if ($insecureFindings.Count -eq 0) {
    $results += New-GateResult -Gate 'security.insecure-defaults' -Level $level -Status 'pass' `
        -Message 'no insecure defaults detected'
} else {
    $details = ($insecureFindings | ForEach-Object { "$($_.File):$($_.Line): $($_.Pattern)" }) -join "`n"
    $results += New-GateResult -Gate 'security.insecure-defaults' -Level $level -Status 'fail' `
        -Message ('{0} finding(s)' -f $insecureFindings.Count) -Details $details
}

# ---- Transport (SHOULD) ----
$nonHttps = @()
foreach ($f in $appsettings) {
    if ($f.Name -match '(?i)Development') { continue }
    $text = Get-Content -LiteralPath $f.FullName -Raw
    $matches = [regex]::Matches($text, '"(http://[^"]+)"')
    foreach ($m in $matches) {
        $url = $m.Groups[1].Value
        if ($url -match 'localhost|127\.0\.0\.1|0\.0\.0\.0') { continue }
        $nonHttps += [pscustomobject]@{
            File = $f.FullName.Replace($RepoRoot, '').TrimStart('\','/')
            Url  = $url
        }
    }
}
if ($nonHttps.Count -eq 0) {
    $results += New-GateResult -Gate 'security.transport' -Level 'SHOULD' -Status 'pass' `
        -Message 'no plain http URLs in non-dev appsettings'
} else {
    $details = ($nonHttps | ForEach-Object { "$($_.File): $($_.Url)" }) -join "`n"
    $results += New-GateResult -Gate 'security.transport' -Level 'SHOULD' -Status 'warn' `
        -Message ('{0} plain http URL(s)' -f $nonHttps.Count) -Details $details
}

return ,$results

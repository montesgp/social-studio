# Architecture gate:
#   1) Layer reference rule: ProjectReference graph respects the inward-pointing rule.
#   2) Forbidden namespace imports per layer (using directives).

[CmdletBinding()]
param(
    [Parameter(Mandatory)] $Config,
    [string] $RepoRoot,
    [string] $CoverageDir   # unused, accepted for orchestrator uniformity
)

. (Join-Path $PSScriptRoot '..\lib\Common.ps1')

$results = @()
$level = $Config.gates.architecture.level
if (-not $level) { $level = 'MUST' }

$arch = $Config.backend.architecture
if (-not $arch) {
    $results += New-GateResult -Gate 'architecture.layering' -Level $level -Status 'skipped' `
        -Message 'backend.architecture not configured'
    return ,$results
}

# Build layer map: project name -> layer
function Add-ProjectsToMap([hashtable] $map, $projects, [string] $layer) {
    if (-not $projects) { return }
    foreach ($p in $projects) {
        $name = Split-Path -Leaf ([string] $p)
        $map[$name] = $layer
    }
}

$layerMap = @{}
Add-ProjectsToMap $layerMap $arch.domain_projects         'domain'
Add-ProjectsToMap $layerMap $arch.application_projects    'application'
Add-ProjectsToMap $layerMap $arch.infrastructure_projects 'infrastructure'
Add-ProjectsToMap $layerMap $arch.api_projects            'api'
Add-ProjectsToMap $layerMap $arch.test_projects           'test'

# Allowed reference matrix: (layer -> set of layers it MAY reference).
# Same-layer references are always allowed (e.g. ApiService -> ServiceDefaults in Aspire,
# AppHost -> ApiService for orchestration). Cycles are caught separately by dependencies gate.
$allowed = @{
    'domain'         = @('domain')
    'application'    = @('domain','application')
    'infrastructure' = @('domain','application','infrastructure')
    'api'            = @('domain','application','infrastructure','api')
    'test'           = @('domain','application','infrastructure','api','test')
}

# ---- Layering check ----
$violations = @()
foreach ($projName in $layerMap.Keys) {
    $projLayer = $layerMap[$projName]
    $projDir = Join-Path $RepoRoot $projName
    if (-not (Test-Path $projDir)) { continue }
    $csprojFiles = Get-ChildItem -Path $projDir -Filter '*.csproj' -File -ErrorAction SilentlyContinue
    foreach ($cs in $csprojFiles) {
        try {
            [xml] $xml = Get-Content -LiteralPath $cs.FullName
        } catch { continue }
        $refs = @()
        $prNodes = $xml.SelectNodes('//ProjectReference')
        foreach ($pr in $prNodes) {
            $include = [string] $pr.Include
            if ($include) {
                $refs += [System.IO.Path]::GetFileNameWithoutExtension($include)
            }
        }
        foreach ($refName in $refs) {
            if (-not $layerMap.ContainsKey($refName)) {
                $violations += [pscustomobject]@{
                    Project   = $projName
                    Layer     = $projLayer
                    Ref       = $refName
                    RefLayer  = '(unknown)'
                    Reason    = 'reference to project outside declared architecture'
                }
                continue
            }
            $refLayer = $layerMap[$refName]
            if ($projLayer -eq 'test') { continue }
            if (-not ($allowed[$projLayer] -contains $refLayer)) {
                $violations += [pscustomobject]@{
                    Project   = $projName
                    Layer     = $projLayer
                    Ref       = $refName
                    RefLayer  = $refLayer
                    Reason    = "layer '$projLayer' MUST NOT reference '$refLayer'"
                }
            }
        }
    }
}

if ($violations.Count -eq 0) {
    $results += New-GateResult -Gate 'architecture.layering' -Level $level -Status 'pass' `
        -Message ('{0} projects checked' -f $layerMap.Count)
} else {
    $msg = '{0} layering violation(s)' -f $violations.Count
    $details = ($violations | ForEach-Object { "$($_.Project) ($($_.Layer)) -> $($_.Ref) ($($_.RefLayer)): $($_.Reason)" }) -join "`n"
    $results += New-GateResult -Gate 'architecture.layering' -Level $level -Status 'fail' `
        -Message $msg -Details $details
}

# ---- Forbidden namespace imports ----
$forbiddenList = $arch.forbidden_namespace_imports
if ($forbiddenList) {
    $importViolations = @()
    foreach ($entry in $forbiddenList) {
        $entryLayer = $entry.layer
        $forbidden = $entry.forbidden
        if (-not $forbidden) { continue }
        $patternRegexes = $forbidden | ForEach-Object { ConvertTo-NamespaceRegex $_ }

        # Files in this layer
        $layerProjects = $layerMap.GetEnumerator() | Where-Object { $_.Value -eq $entryLayer } | Select-Object -ExpandProperty Key
        foreach ($proj in $layerProjects) {
            $projDir = Join-Path $RepoRoot $proj
            if (-not (Test-Path $projDir)) { continue }
            $csFiles = Get-ChildItem -Path $projDir -Filter '*.cs' -File -Recurse -ErrorAction SilentlyContinue |
                Where-Object {
                    ($_.FullName -notmatch '[\\/]bin[\\/]') -and
                    ($_.FullName -notmatch '[\\/]obj[\\/]')
                }
            foreach ($file in $csFiles) {
                $lineNo = 0
                $lines = Get-Content -LiteralPath $file.FullName
                foreach ($line in $lines) {
                    $lineNo++
                    foreach ($rx in $patternRegexes) {
                        if ($line -match $rx) {
                            $importViolations += [pscustomobject]@{
                                Layer = $entryLayer
                                File  = $file.FullName.Replace($RepoRoot, '').TrimStart('\','/')
                                Line  = $lineNo
                                Using = $line.Trim()
                            }
                        }
                    }
                }
            }
        }
    }

    if ($importViolations.Count -eq 0) {
        $results += New-GateResult -Gate 'architecture.imports' -Level $level -Status 'pass' `
            -Message 'no forbidden namespace imports'
    } else {
        $msg = '{0} forbidden import(s)' -f $importViolations.Count
        $details = ($importViolations | ForEach-Object { "[$($_.Layer)] $($_.File):$($_.Line): $($_.Using)" }) -join "`n"
        $results += New-GateResult -Gate 'architecture.imports' -Level $level -Status 'fail' `
            -Message $msg -Details $details
    }
}

return ,$results

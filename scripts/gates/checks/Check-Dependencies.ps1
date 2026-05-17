# Dependencies gate:
#   1) Cycle detection on the project reference graph.
#   2) Solution drift: declared projects must exist in the solution; unlisted projects warn.
#   3) Reciprocity sanity: InternalsVisibleTo targets must be referenced.

[CmdletBinding()]
param(
    [Parameter(Mandatory)] $Config,
    [string] $RepoRoot,
    [string] $CoverageDir   # unused, accepted for orchestrator uniformity
)

. (Join-Path $PSScriptRoot '..\lib\Common.ps1')

$results = @()
$level = $Config.gates.dependencies.level
if (-not $level) { $level = 'MUST' }
$allowUnlisted = $false
if ($Config.gates.dependencies -and $Config.gates.dependencies.PSObject.Properties.Name -contains 'allow_unlisted_projects') {
    $allowUnlisted = [bool] $Config.gates.dependencies.allow_unlisted_projects
}

$arch = $Config.backend.architecture
$declared = @()
foreach ($key in @('domain_projects','application_projects','infrastructure_projects','api_projects','test_projects')) {
    if ($arch.$key) { $declared += $arch.$key }
}

# Build graph from .csproj files in declared projects
$graph = @{}
foreach ($proj in $declared) {
    $projDir = Join-Path $RepoRoot $proj
    $csproj = Get-ChildItem -Path $projDir -Filter '*.csproj' -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $csproj) { continue }
    [xml] $xml = Get-Content -LiteralPath $csproj.FullName
    $refs = @()
    foreach ($pr in $xml.SelectNodes('//ProjectReference')) {
        $include = [string] $pr.Include
        if ($include) { $refs += [System.IO.Path]::GetFileNameWithoutExtension($include) }
    }
    $graph[$proj] = $refs
}

# Cycle detection (DFS w/ coloring)
$WHITE=0; $GRAY=1; $BLACK=2
$color = @{}
foreach ($n in $graph.Keys) { $color[$n] = $WHITE }
$cycles = @()

function Find-Cycle($node, $stack) {
    $color[$node] = $GRAY
    $stack += $node
    foreach ($n in $graph[$node]) {
        if (-not $color.ContainsKey($n)) {
            $color[$n] = $WHITE  # external project (still in graph as edge)
            $graph[$n] = @()
        }
        if ($color[$n] -eq $GRAY) {
            # cycle found
            $startIdx = [Array]::IndexOf($stack, $n)
            if ($startIdx -ge 0) {
                $script:cycles += ,(($stack[$startIdx..($stack.Count - 1)] + @($n)) -join ' -> ')
            } else {
                $script:cycles += ,(($stack + @($n)) -join ' -> ')
            }
        } elseif ($color[$n] -eq $WHITE) {
            Find-Cycle $n $stack
        }
    }
    $color[$node] = $BLACK
}

$script:cycles = @()
foreach ($n in @($graph.Keys)) {
    if ($color[$n] -eq $WHITE) { Find-Cycle $n @() }
}

if ($script:cycles.Count -eq 0) {
    $results += New-GateResult -Gate 'dependencies.cycles' -Level $level -Status 'pass' `
        -Message ('no cycles ({0} projects)' -f $graph.Count)
} else {
    $results += New-GateResult -Gate 'dependencies.cycles' -Level $level -Status 'fail' `
        -Message ('{0} cycle(s) detected' -f $script:cycles.Count) `
        -Details ($script:cycles -join "`n")
}

# Solution drift
$solRel = $Config.backend.solution
$solPath = Join-Path $RepoRoot $solRel
if (-not (Test-Path $solPath)) {
    $results += New-GateResult -Gate 'dependencies.solution-drift' -Level $level -Status 'fail' `
        -Message "solution not found: $solRel"
} else {
    $solContent = Get-Content -LiteralPath $solPath -Raw
    $missing = @()
    foreach ($proj in $declared) {
        $leaf = Split-Path -Leaf $proj
        $csproj = "$leaf.csproj"
        if ($solContent -notmatch [regex]::Escape($csproj)) {
            $missing += $proj
        }
    }

    # Find projects mentioned in solution but not in declared
    $solProjects = @()
    if ($solRel -like '*.slnx') {
        try {
            [xml] $solXml = Get-Content -LiteralPath $solPath
            foreach ($p in $solXml.SelectNodes('//Project')) {
                $path = [string] $p.Path
                if ($path) {
                    $solProjects += [System.IO.Path]::GetFileNameWithoutExtension($path)
                }
            }
        } catch { }
    } else {
        # legacy .sln: parse Project lines
        $matches = [regex]::Matches($solContent, 'Project\("[^"]+"\)\s*=\s*"([^"]+)",\s*"([^"]+)"')
        foreach ($m in $matches) {
            $solProjects += [System.IO.Path]::GetFileNameWithoutExtension($m.Groups[2].Value)
        }
    }
    $declaredLeafs = $declared | ForEach-Object { Split-Path -Leaf $_ }
    $unlisted = $solProjects | Where-Object { $declaredLeafs -notcontains $_ }

    $stat = 'pass'
    $msgParts = @()
    if ($missing.Count -gt 0) {
        $stat = 'fail'
        $msgParts += "$($missing.Count) declared but not in solution"
    }
    if ($unlisted.Count -gt 0 -and -not $allowUnlisted) {
        if ($stat -ne 'fail') { $stat = 'warn' }
        $msgParts += "$($unlisted.Count) in solution but not declared"
    }
    if ($msgParts.Count -eq 0) { $msgParts += 'all projects declared and present' }

    $details = @()
    if ($missing.Count -gt 0)  { $details += 'missing: ' + ($missing -join ', ') }
    if ($unlisted.Count -gt 0) { $details += 'unlisted: ' + ($unlisted -join ', ') }

    # If 'warn' but level is MUST, still don't fail (drift warnings are advisory unless missing)
    $effectiveStatus = $stat
    if ($stat -eq 'warn' -and $level -eq 'MUST') {
        # downgrade reporting - unlisted projects are not a hard failure
        $effectiveStatus = 'warn'
    }
    $results += New-GateResult -Gate 'dependencies.solution-drift' -Level $level -Status $effectiveStatus `
        -Message ($msgParts -join '; ') -Details ($details -join "`n")
}

# Reciprocity sanity (SHOULD): IVT targets present somewhere in graph
$ivtViolations = @()
foreach ($proj in $declared) {
    $projDir = Join-Path $RepoRoot $proj
    $csproj = Get-ChildItem -Path $projDir -Filter '*.csproj' -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $csproj) { continue }
    [xml] $xml = Get-Content -LiteralPath $csproj.FullName
    $ivts = @()
    foreach ($i in $xml.SelectNodes('//InternalsVisibleTo')) {
        $ivts += [string] $i.Include
    }
    foreach ($t in $ivts) {
        # IVT to T means T should consume our internals -> T must reference us.
        if ($graph.ContainsKey($t)) {
            if ($graph[$t] -contains $proj) { continue }
            $ivtViolations += "$proj exposes IVT to '$t' but '$t' does not reference '$proj'"
        } else {
            # Target project not declared - flag as orphan.
            $ivtViolations += "$proj exposes IVT to '$t' but '$t' is not in the declared graph"
        }
    }
}
if ($ivtViolations.Count -eq 0) {
    $results += New-GateResult -Gate 'dependencies.reciprocity' -Level 'SHOULD' -Status 'pass' `
        -Message 'no orphan InternalsVisibleTo'
} else {
    $results += New-GateResult -Gate 'dependencies.reciprocity' -Level 'SHOULD' -Status 'warn' `
        -Message ('{0} orphan IVT' -f $ivtViolations.Count) -Details ($ivtViolations -join "`n")
}

return ,$results

# Minimal YAML loader for project.gates.yml.
# Supports the subset used by Phase 1: scalars, mappings, sequences (- item),
# nested objects via 2-space indentation, comments (#...), quoted strings,
# numbers, booleans, null, and inline arrays [a, b, c].
#
# It is NOT a general YAML parser. Keep project.gates.yml within the supported subset.
# If the schema grows, replace this with the powershell-yaml module.

$ErrorActionPreference = 'Stop'

function ConvertFrom-YamlScalar {
    param([string] $Raw)
    if ($null -eq $Raw) { return $null }
    $v = $Raw.Trim()
    if ($v -eq '' -or $v -eq '~' -or $v -ieq 'null') { return $null }
    if ($v -ieq 'true')  { return $true }
    if ($v -ieq 'false') { return $false }
    if ($v.Length -ge 2) {
        if (($v.StartsWith('"') -and $v.EndsWith('"')) -or
            ($v.StartsWith("'") -and $v.EndsWith("'"))) {
            return $v.Substring(1, $v.Length - 2)
        }
    }
    if ($v.StartsWith('[') -and $v.EndsWith(']')) {
        $inner = $v.Substring(1, $v.Length - 2).Trim()
        $arr = New-Object System.Collections.Generic.List[object]
        if ($inner -ne '') {
            foreach ($x in $inner.Split(',')) { [void] $arr.Add((ConvertFrom-YamlScalar $x)) }
        }
        return ,$arr
    }
    [int] $intVal = 0
    if ([int]::TryParse($v, [ref] $intVal)) { return $intVal }
    [double] $dblVal = 0
    if ([double]::TryParse($v, [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture, [ref] $dblVal)) {
        return $dblVal
    }
    return $v
}

function ConvertFrom-Yaml {
    param([Parameter(Mandatory)] [string] $Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "YAML file not found: $Path"
    }
    $rawLines = Get-Content -LiteralPath $Path
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($l in $rawLines) {
        $stripped = $l
        $hash = $stripped.IndexOf('#')
        if ($hash -ge 0) {
            $before = $stripped.Substring(0, $hash)
            $sQuotes = ([regex]::Matches($before, "'")).Count
            $dQuotes = ([regex]::Matches($before, '"')).Count
            if (($sQuotes % 2 -eq 0) -and ($dQuotes % 2 -eq 0)) {
                $stripped = $before
            }
        }
        if ($stripped.Trim() -ne '') {
            [void] $lines.Add($stripped.TrimEnd())
        }
    }

    $script:idx = 0
    $script:src = $lines

    function script:Get-Indent($s) {
        $i = 0
        while ($i -lt $s.Length -and $s[$i] -eq ' ') { $i++ }
        return $i
    }

    # Wrapped result: { Kind = 'map'|'list'|'null'; Value = <object> }
    function script:Parse-Block($baseIndent) {
        $kind = $null
        $map  = $null
        $list = $null

        while ($script:idx -lt $script:src.Count) {
            $line = $script:src[$script:idx]
            $indent = script:Get-Indent $line
            if ($indent -lt $baseIndent) { break }
            $content = $line.Substring($indent)

            if ($content.StartsWith('- ')) {
                if ($null -eq $kind) {
                    $kind = 'list'
                    $list = New-Object System.Collections.Generic.List[object]
                } elseif ($kind -ne 'list') {
                    throw "YAML parse error at line $($script:idx + 1): mixing list and map"
                }
                $itemContent = $content.Substring(2)
                $script:idx++
                if ($itemContent -match '^([^:]+):\s*(.*)$') {
                    # list item is itself a map; rewrite line to "<spaces>key: val" and reparse
                    $script:idx--
                    $newLine = (' ' * ($indent + 2)) + ($content.Substring(2))
                    $script:src[$script:idx] = $newLine
                    $sub = script:Parse-Block ($indent + 2)
                    [void] $list.Add($sub)
                } else {
                    [void] $list.Add((ConvertFrom-YamlScalar $itemContent))
                }
            }
            elseif ($content -match '^([^:]+):\s*(.*)$') {
                $key = $Matches[1].Trim()
                $valueRaw = $Matches[2]
                if ($null -eq $kind) {
                    $kind = 'map'
                    $map = [ordered] @{}
                } elseif ($kind -ne 'map') {
                    throw "YAML parse error at line $($script:idx + 1): mixing list and map"
                }
                $script:idx++
                if ($valueRaw -ne '' -and $null -ne $valueRaw) {
                    $map[$key] = ConvertFrom-YamlScalar $valueRaw
                } else {
                    if ($script:idx -lt $script:src.Count) {
                        $nextLine = $script:src[$script:idx]
                        $nextIndent = script:Get-Indent $nextLine
                        if ($nextIndent -gt $indent) {
                            $map[$key] = script:Parse-Block $nextIndent
                            continue
                        }
                    }
                    $map[$key] = $null
                }
            }
            else {
                $script:idx++
            }
        }

        if ($kind -eq 'list') { return ,$list }
        if ($kind -eq 'map')  { return $map }
        return $null
    }

    # Wrap top-level call to defeat PS pipeline unwrap of single-element lists
    $top = $null
    $top = (script:Parse-Block 0)
    return ,$top | Select-Object -First 1
}

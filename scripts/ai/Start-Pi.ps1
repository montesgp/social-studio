[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$envFile = Join-Path $repoRoot ".env.local"
$npmGlobalBin = Join-Path $env:APPDATA "npm"

if (Test-Path $npmGlobalBin) {
    if (-not ($env:PATH -split ';' | Where-Object { $_ -eq $npmGlobalBin })) {
        $env:PATH = "$npmGlobalBin;$env:PATH"
    }
}

if (Test-Path $envFile) {
    Get-Content $envFile | ForEach-Object {
        $line = $_.Trim()
        if ($line -and -not $line.StartsWith('#')) {
            $separatorIndex = $line.IndexOf('=')
            if ($separatorIndex -ge 1) {
                $key = $line.Substring(0, $separatorIndex).Trim()
                $value = $line.Substring($separatorIndex + 1).Trim()

                if ($value.StartsWith('"') -and $value.EndsWith('"')) {
                    $value = $value.Substring(1, $value.Length - 2)
                }

                Set-Item -Path "Env:$key" -Value $value
            }
        }
    }
}

$piCommand = Get-Command pi -ErrorAction SilentlyContinue
if (-not $piCommand) {
    throw "Pi is not available on PATH. Add '$npmGlobalBin' to PATH or install Pi globally before running this script."
}

Write-Host "Launching Pi in $repoRoot" -ForegroundColor Cyan
Push-Location $repoRoot
try {
    & $piCommand.Source
}
finally {
    Pop-Location
}

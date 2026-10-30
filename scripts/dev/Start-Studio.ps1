# One-click local stack launcher for Social Studio.
#
# Brings up every local dependency the product needs, in order, and skips anything that is
# already running (safe to run twice):
#   1. Tooling check   - dotnet 10 SDK, Node.js + pnpm (activated through fnm when needed), Docker CLI
#   2. Docker engine   - starts Docker Desktop when the engine is down and waits for it
#   3. Postgres        - docker-compose.dev.yml on 127.0.0.1:5433 (via Start-LocalDb.ps1, which
#                        also refuses to continue when user-secrets point at a non-local database)
#   4. Chromium        - Playwright browser required by the slide renderer
#   5. ComfyUI         - local image generation backend on 127.0.0.1:8188
#   6. Backend         - Aspire AppHost -> ApiService on http://localhost:5336
#   7. Frontend        - Next.js dev server on http://localhost:3000
#
# Supabase is NOT started locally: development uses the local Docker Postgres and
# Storage:AssetProvider=Local, so no Supabase service is required on this machine.
#
# Usage (from the repository root):
#   pwsh -File scripts/dev/Start-Studio.ps1
#   pwsh -File scripts/dev/Start-Studio.ps1 -FullStartup      # seed data + file logging (AppHost "http" profile)
#   pwsh -File scripts/dev/Start-Studio.ps1 -SkipComfyUI      # no image generation backend
#   pwsh -File scripts/dev/Start-Studio.ps1 -RecreateDb       # wipe the local DB volume first
# Or double-click scripts/dev/start-studio.bat.
#
# ComfyUI location: -ComfyUIRoot, else $env:HIVE_COMFYUI_ROOT, else the first known portable
# install found under the user profile. Launch flags: -ComfyUIArgs, else $env:HIVE_COMFYUI_ARGS,
# else chosen from the detected GPU (NVIDIA -> CUDA, AMD -> DirectML, otherwise CPU).
# Install guide: docs/infrastructure/comfyui/setup.md

[CmdletBinding()]
param(
    [switch] $SkipComfyUI,
    [switch] $SkipBackend,
    [switch] $SkipFrontend,
    [switch] $NoBrowser,
    # Use the AppHost "http" profile (seed data + file logging) instead of "http-fast".
    [switch] $FullStartup,
    # Drop the local Postgres volume so the database starts empty.
    [switch] $RecreateDb,
    [string] $ComfyUIRoot = $env:HIVE_COMFYUI_ROOT,
    [string] $ComfyUIArgs = $env:HIVE_COMFYUI_ARGS,
    # Seconds to wait for every service to answer before giving up on the readiness report.
    [int] $ReadyTimeoutSeconds = 420
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$webRoot = Join-Path $repoRoot 'apps' 'web'
$appHostProject = Join-Path $repoRoot 'SocialStudio.AppHost' 'SocialStudio.AppHost.csproj'
$logsDir = Join-Path $repoRoot 'logs'
New-Item -ItemType Directory -Path $logsDir -Force | Out-Null

$apiUrl = 'http://localhost:5336'
$webUrl = 'http://localhost:3000'
$comfyUrl = 'http://127.0.0.1:8188'
$aspireDashboardUrl = 'http://localhost:15161'

$Host.UI.RawUI.WindowTitle = 'Social Studio - local stack launcher'

function Write-Step([string] $msg) { Write-Host ''; Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-Ok([string] $msg) { Write-Host "    $msg" -ForegroundColor Green }
function Write-Info([string] $msg) { Write-Host "    $msg" }
function Write-Warn([string] $msg) { Write-Host "    WARNING: $msg" -ForegroundColor Yellow }

function Test-Port([string] $hostName, [int] $port) {
    $client = [System.Net.Sockets.TcpClient]::new()
    try {
        return $client.ConnectAsync($hostName, $port).Wait(500) -and $client.Connected
    } catch {
        return $false
    } finally {
        $client.Dispose()
    }
}

function Test-Http([string] $url) {
    try {
        $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 3 -SkipHttpErrorCheck
        return $response.StatusCode -lt 500
    } catch {
        return $false
    }
}

function Start-ServiceWindow([string] $title, [string] $workingDir, [string] $command) {
    # Each long-running process gets its own console so its logs stay visible; closing the
    # window stops the process. The child inherits this session's PATH (fnm/node included).
    $script = "`$Host.UI.RawUI.WindowTitle = '$title'; Set-Location -LiteralPath '$workingDir'; $command"
    Start-Process -FilePath 'pwsh' -WorkingDirectory $workingDir `
        -ArgumentList @('-NoProfile', '-NoExit', '-ExecutionPolicy', 'Bypass', '-Command', $script) | Out-Null
}

# --- 1. Tooling ------------------------------------------------------------------
Write-Step 'Checking tooling'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet is not on PATH. Install the .NET 10 SDK: winget install Microsoft.DotNet.SDK.10'
}
if (-not (dotnet --list-sdks | Select-String -Pattern '^10\.')) {
    throw 'The .NET 10 SDK is required (projects target net10.0). Install it: winget install Microsoft.DotNet.SDK.10'
}
Write-Ok "dotnet $(dotnet --version)"

if (-not $SkipFrontend) {
    # Double-click launches run without the user's profile, so fnm-managed Node is not on PATH yet.
    # Prefer the fnm default over any machine-wide Node so the version matches the usual shell.
    if (-not $env:FNM_MULTISHELL_PATH -and (Get-Command fnm -ErrorAction SilentlyContinue)) {
        fnm env --shell powershell | Out-String | Invoke-Expression
        fnm use default --silent-if-unchanged *> $null
    }
    if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
        throw 'Node.js is not on PATH. Install Node.js 22 LTS (for example: fnm install 22 && fnm default 22).'
    }
    if (-not (Get-Command pnpm -ErrorAction SilentlyContinue)) {
        throw 'pnpm is not on PATH. Enable it with: corepack enable pnpm   (or: npm install -g pnpm)'
    }
    Write-Ok "node $(node -v), pnpm $(pnpm -v)"
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw 'Docker is not on PATH. Install Docker Desktop: winget install Docker.DockerDesktop'
}

# --- 2. Docker engine --------------------------------------------------------------
Write-Step 'Checking Docker engine'

docker info *> $null
if ($LASTEXITCODE -ne 0) {
    Write-Info 'Docker engine is down. Starting Docker Desktop...'
    docker desktop start --detach *> $null
    if ($LASTEXITCODE -ne 0) {
        # Older Docker Desktop builds have no "docker desktop" CLI plugin.
        $dockerDesktopExe = Join-Path $env:ProgramFiles 'Docker' 'Docker' 'Docker Desktop.exe'
        if (-not (Test-Path -LiteralPath $dockerDesktopExe)) {
            throw "Could not start Docker Desktop and it was not found at $dockerDesktopExe. Start it manually and retry."
        }
        Start-Process -FilePath $dockerDesktopExe | Out-Null
    }
    $deadline = (Get-Date).AddSeconds(180)
    do {
        Start-Sleep -Seconds 3
        docker info *> $null
    } while ($LASTEXITCODE -ne 0 -and (Get-Date) -lt $deadline)
    if ($LASTEXITCODE -ne 0) {
        throw 'Docker engine did not become ready within 180s. Open Docker Desktop, check for errors (CPU virtualization must be enabled), and retry.'
    }
}
Write-Ok "Docker engine $(docker info --format '{{.ServerVersion}}') is running"

# --- 3. Postgres -------------------------------------------------------------------
Write-Step 'Starting local Postgres'
& (Join-Path $PSScriptRoot 'Start-LocalDb.ps1') -Recreate:$RecreateDb

# --- 4. Chromium (Playwright) ------------------------------------------------------
if (-not $SkipBackend) {
    Write-Step 'Checking Playwright Chromium (slide renderer)'
    $playwrightBrowsers = if ($env:PLAYWRIGHT_BROWSERS_PATH) { $env:PLAYWRIGHT_BROWSERS_PATH } else { Join-Path $env:LOCALAPPDATA 'ms-playwright' }
    if (Get-ChildItem -LiteralPath $playwrightBrowsers -Directory -Filter 'chromium-*' -ErrorAction SilentlyContinue) {
        Write-Ok "Chromium found in $playwrightBrowsers"
    } else {
        Write-Info 'Chromium is missing. Building the API to get the Playwright installer...'
        dotnet build (Join-Path $repoRoot 'SocialStudio.ApiService' 'SocialStudio.ApiService.csproj') -v quiet -nologo
        $installer = Get-ChildItem -Path (Join-Path $repoRoot 'SocialStudio.ApiService' 'bin') -Recurse -Filter 'playwright.ps1' -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($installer) {
            & $installer.FullName install chromium
            Write-Ok 'Chromium installed'
        } else {
            Write-Warn 'playwright.ps1 was not produced by the build; slide rendering will fail until you run: playwright.ps1 install chromium'
        }
    }
}

# --- 5. ComfyUI --------------------------------------------------------------------
$comfyStarted = $false
if ($SkipComfyUI) {
    Write-Step 'ComfyUI skipped (-SkipComfyUI)'
} else {
    Write-Step 'Checking ComfyUI'
    if (Test-Http "$comfyUrl/system_stats") {
        Write-Ok "ComfyUI is already running on $comfyUrl"
    } else {
        $candidates = @(
            $ComfyUIRoot,
            (Join-Path $env:USERPROFILE 'Downloads' 'ComfyUI_windows_portable_nvidia' 'ComfyUI_windows_portable'),
            (Join-Path $env:USERPROFILE 'ComfyUI_windows_portable'),
            'C:\ComfyUI_windows_portable',
            'D:\ComfyUI_windows_portable'
        ) | Where-Object { $_ }
        $comfyRoot = $candidates |
            Where-Object { Test-Path -LiteralPath (Join-Path $_ 'python_embeded' 'python.exe') } |
            Select-Object -First 1

        if (-not $comfyRoot) {
            Write-Warn 'ComfyUI portable install not found. Image generation will be unavailable.'
            Write-Info 'Install it following docs/infrastructure/comfyui/setup.md, then set HIVE_COMFYUI_ROOT to its folder.'
        } else {
            if (-not $ComfyUIArgs) {
                $gpus = (Get-CimInstance Win32_VideoController -ErrorAction SilentlyContinue).Name -join ' '
                $ComfyUIArgs = switch -Regex ($gpus) {
                    'NVIDIA' { '--windows-standalone-build'; break }
                    'AMD|Radeon' { '--directml --lowvram --cpu-vae --use-split-cross-attention'; break }
                    default { '--cpu --windows-standalone-build' }
                }
                Write-Info "GPU: $gpus -> ComfyUI flags: $ComfyUIArgs"
            }
            Start-ServiceWindow 'Social Studio - ComfyUI' $comfyRoot ".\python_embeded\python.exe -s ComfyUI\main.py $ComfyUIArgs"
            $comfyStarted = $true
            Write-Ok "ComfyUI starting from $comfyRoot"
        }
    }
}

# --- 6. Backend --------------------------------------------------------------------
if ($SkipBackend) {
    Write-Step 'Backend skipped (-SkipBackend)'
} else {
    Write-Step 'Starting backend (Aspire AppHost)'
    if (Test-Port 'localhost' 5336) {
        Write-Ok "Something is already listening on $apiUrl - assuming the backend is running"
    } else {
        $launchProfile = if ($FullStartup) { 'http' } else { 'http-fast' }
        $backendLog = Join-Path $logsDir 'launcher-backend.log'
        Start-ServiceWindow 'Social Studio - Backend' $repoRoot `
            "`$env:ASPIRE_ALLOW_UNSECURED_TRANSPORT = 'true'; dotnet run --project '$appHostProject' --launch-profile $launchProfile 2>&1 | Tee-Object -FilePath '$backendLog'"
        Write-Ok "AppHost starting with profile '$launchProfile' (output also in logs/launcher-backend.log)"
    }
}

# --- 7. Frontend -------------------------------------------------------------------
if ($SkipFrontend) {
    Write-Step 'Frontend skipped (-SkipFrontend)'
} else {
    Write-Step 'Starting frontend (Next.js)'
    if (Test-Port 'localhost' 3000) {
        Write-Ok "Something is already listening on $webUrl - assuming the frontend is running"
    } else {
        $envLocal = Join-Path $webRoot '.env.local'
        if (-not (Test-Path -LiteralPath $envLocal)) {
            Copy-Item -LiteralPath (Join-Path $webRoot '.env.example') -Destination $envLocal
            Write-Info 'Created apps/web/.env.local from .env.example'
        }

        # Reinstall when node_modules is missing or older than the lockfile (e.g. after a pull).
        $lock = Get-Item -LiteralPath (Join-Path $webRoot 'pnpm-lock.yaml')
        $installed = Join-Path $webRoot 'node_modules' '.modules.yaml'
        if (-not (Test-Path -LiteralPath $installed) -or (Get-Item -LiteralPath $installed).LastWriteTime -lt $lock.LastWriteTime) {
            Write-Info 'node_modules is missing or out of date; running pnpm install...'
            Push-Location $webRoot
            try {
                pnpm install --frozen-lockfile
                if ($LASTEXITCODE -ne 0) { throw "pnpm install failed with exit code $LASTEXITCODE" }
                # pnpm leaves the marker untouched when nothing changed; stamp it so the check passes next time.
                if (Test-Path -LiteralPath $installed) { (Get-Item -LiteralPath $installed).LastWriteTime = Get-Date }
            } finally {
                Pop-Location
            }
        }
        Start-ServiceWindow 'Social Studio - Frontend' $webRoot 'pnpm run dev:fast'
        Write-Ok 'Next.js dev server starting'
    }
}

# --- Readiness report --------------------------------------------------------------
Write-Step "Waiting for services (up to $ReadyTimeoutSeconds s; first builds are slow)"
$checks = [ordered]@{}
if (-not $SkipBackend) { $checks['Backend'] = "$apiUrl/health" }
if (-not $SkipFrontend) { $checks['Frontend'] = $webUrl }
if ($comfyStarted) { $checks['ComfyUI'] = "$comfyUrl/system_stats" }

$pending = [System.Collections.Generic.List[string]]::new([string[]]$checks.Keys)
$deadline = (Get-Date).AddSeconds($ReadyTimeoutSeconds)
while ($pending.Count -gt 0 -and (Get-Date) -lt $deadline) {
    foreach ($name in @($pending)) {
        if (Test-Http $checks[$name]) {
            Write-Ok "$name is ready"
            $pending.Remove($name) | Out-Null
        }
    }
    if ($pending.Count -gt 0) { Start-Sleep -Seconds 3 }
}
foreach ($name in $pending) {
    Write-Warn "$name did not answer at $($checks[$name]) yet - check its console window."
}

Write-Host ''
Write-Host 'Social Studio local stack' -ForegroundColor Green
Write-Info "Frontend:         $webUrl/login"
Write-Info "API:              $apiUrl"
Write-Info "Aspire dashboard: $aspireDashboardUrl"
Write-Info "ComfyUI:          $comfyUrl"
Write-Info 'Postgres:         127.0.0.1:5433 (container social-studio-dev-postgres)'
Write-Info 'Close a service console window to stop that service.'
Write-Info 'Stop Postgres with: docker compose -f docker-compose.dev.yml stop'

if (-not $NoBrowser -and -not $SkipFrontend -and -not $pending.Contains('Frontend')) {
    Start-Process "$webUrl/login" | Out-Null
}

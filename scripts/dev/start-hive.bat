@echo off
rem Double-click entry point for the Incoders Hive local stack.
rem Hands off to Start-Hive.ps1, which starts Docker, Postgres, ComfyUI, backend and frontend.
rem Arguments are forwarded, e.g.: start-hive.bat -SkipComfyUI -FullStartup
setlocal

title Incoders Hive - local stack launcher

where pwsh >nul 2>nul
if errorlevel 1 (
  echo ERROR: PowerShell 7 ^(pwsh^) is not available on PATH.
  echo Install it with: winget install Microsoft.PowerShell
  goto :fail
)

pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-Hive.ps1" %*
if errorlevel 1 goto :fail

echo.
echo Launcher finished. Service consoles stay open; close them to stop each service.
pause
exit /b 0

:fail
echo.
echo Launcher could not start the complete stack. Check the message above.
pause
exit /b 1

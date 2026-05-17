@echo off
echo Creating auth page directories and files...
node "%~dp0create-auth-pages.js"
if %ERRORLEVEL% neq 0 (
    echo ERROR: Node.js script failed. Make sure Node.js is installed.
    pause
    exit /b 1
)
echo.
echo Done! You can delete this file and scripts\create-auth-pages.js now.
pause

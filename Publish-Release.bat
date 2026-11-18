@echo off
setlocal enabledelayedexpansion

echo ==========================================================
echo               VibeSkua Release Packager
echo ==========================================================
echo.

:: 1. Check for Velopack CLI (vpk)
where vpk >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo [1/4] Installing Velopack CLI ^(vpk^)...
    dotnet tool install -g vpk
    if %ERRORLEVEL% NEQ 0 (
        echo [ERROR] Failed to install Velopack CLI.
        pause
        exit /b 1
    )
) else (
    echo [1/4] Velopack CLI ^(vpk^) is ready.
)

:: 2. Determine Version
set "VERSION=%~1"
if "%VERSION%"=="" (
    for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "[xml]$x = Get-Content '%~dp0Directory.Build.props'; $x.Project.PropertyGroup.Version | Select-Object -First 1"`) do (
        set "VERSION=%%v"
    )
)

if "%VERSION%"=="" set "VERSION=2.0.0"
echo [2/4] Packaging version: %VERSION%
echo.

:: 3. Build & Assemble Project Output
echo [3/4] Running BuildProject.bat...
call "%~dp0BuildProject.bat" --no-pause
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Build failed. Aborting release package.
    pause
    exit /b %ERRORLEVEL%
)

:: 4. Clean old releases and run vpk pack
echo.
echo [4/4] Generating Release Assets with Velopack...
if exist "%~dp0Build\Releases" (
    rmdir /s /q "%~dp0Build\Releases" >nul 2>&1
)

vpk pack -u VibeSkua -v %VERSION% -p "%~dp0Build" -e VibeSkua.Manager.exe --icon "%~dp0Skua.Shared.Avalonia\Assets\SkuaIcon.ico" -o "%~dp0Build\Releases" -r win-x64
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Velopack packaging failed.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo ==========================================================
echo           VibeSkua Release Packaging Complete!
echo ==========================================================
echo.
echo Output location: "%~dp0Build\Releases"
echo.
echo Generated Assets:
echo   [1] VibeSkua-win-Setup.exe      - 1-click installer for new users
echo   [2] VibeSkua-win-Portable.zip   - Zero-install portable zip
echo   [3] VibeSkua-%VERSION%-full.nupkg - Background auto-update payload
echo   [4] releases.win.json           - Update manifest for GitHub Releases
echo.
echo Next steps:
echo   1. Push the intended source commit to the repository used for releases.
echo   2. Push tag 'v%VERSION%' to trigger the automated GitHub Actions release workflow.
echo   3. For manual publishing, upload the files from Build\Releases\ to the matching GitHub release.
echo ==========================================================
echo.
pause

@echo off
setlocal EnableExtensions

rem Run a temporary copy so git pull can safely update this script on disk.
if defined BUILDPROJECT_RUNNING goto :BuildRunner

set "BUILDPROJECT_ROOT=%~dp0"
if "%~1"=="" set "BUILDPROJECT_PAUSE=1"
set "BUILDPROJECT_RUNNING=1"
set "BUILDPROJECT_TEMP=%TEMP%\BuildProject-%RANDOM%-%RANDOM%.bat"

copy /Y "%~f0" "%BUILDPROJECT_TEMP%" >nul
if errorlevel 1 (
    echo Failed to create a temporary copy of the build script.
    pause
    exit /b 1
)

call "%BUILDPROJECT_TEMP%"
set "BUILDPROJECT_EXIT=%ERRORLEVEL%"
del "%BUILDPROJECT_TEMP%" >nul 2>&1
exit /b %BUILDPROJECT_EXIT%

:BuildRunner
pushd "%BUILDPROJECT_ROOT%" || goto :Failure

echo Checking repository state...
for /f "delims=" %%B in ('git branch --show-current 2^>nul') do set "BUILDPROJECT_BRANCH=%%B"

git status --porcelain >nul 2>&1
if errorlevel 1 (
    echo Git status failed. Make sure this is a valid Git repository.
    goto :Failure
)

if "%BUILDPROJECT_BRANCH%"=="main" (
    for /f "delims=" %%S in ('git status --porcelain ^| findstr /V /C:"Skua.AS3/skua/bin/skua.swf"') do (
        echo Your working tree has uncommitted changes:
        git status --short
        echo Only changes to Skua.AS3/skua/bin/skua.swf are ignored by this build check.
        echo Commit or stash other changes before syncing main.
        goto :Failure
    )

    git remote get-url origin >nul 2>&1
    if errorlevel 1 (
        echo Missing Git remote 'origin' for wtffidy/VibeSkua.
        goto :Failure
    )
    git remote get-url upstream >nul 2>&1
    if errorlevel 1 (
        echo Missing Git remote 'upstream' for NinjaXz/VibeSkua.
        echo Add it with: git remote add upstream https://github.com/NinjaXz/VibeSkua.git
        goto :Failure
    )

    echo.
    echo [1/3] Pulling the latest changes from your fork...
    git pull --ff-only origin main
    if errorlevel 1 (
        echo Failed to pull origin/main. Resolve the Git issue before building.
        goto :Failure
    )

    echo.
    echo [2/3] Syncing your fork with NinjaXz/VibeSkua...
    git fetch upstream
    if errorlevel 1 (
        echo Failed to fetch upstream changes.
        goto :Failure
    )

    git merge --no-edit upstream/main
    if errorlevel 1 (
        echo Upstream merge failed or has conflicts. Attempting to cancel the incomplete merge...
        git merge --abort >nul 2>&1
        echo Resolve the sync issue manually, then run BuildProject.bat again.
        goto :Failure
    )

    git push origin main
    if errorlevel 1 (
        echo Failed to push the synced main branch to your fork.
        echo Your local repository may now contain the upstream merge. Fix authentication or push issues, then rerun.
        goto :Failure
    )

    echo Fork sync complete.
) else (
    echo Building current checkout "%BUILDPROJECT_BRANCH%" without syncing main.
    echo Feature-branch and local review changes will be left untouched.
)

echo Closing any running instances...
for %%P in (VibeSkua.exe VibeSkua.Manager.exe Skua.exe Skua.Manager.exe) do taskkill /IM "%%P" /F >nul 2>&1

if not exist "Build" (
    echo Preparing Build folder...
    mkdir "Build"
    if errorlevel 1 goto :Failure
)

set "AS3_REBUILD=0"
set "AS3_TEMP_SWF=Skua.AS3\skua\bin\skua.build-%RANDOM%-%RANDOM%.tmp.swf"
if not exist "Skua.AS3\skua\bin\skua.swf" set "AS3_REBUILD=1"
git diff --quiet HEAD -- "Skua.AS3/skua/bin/skua.swf"
if errorlevel 1 set "AS3_REBUILD=1"
if "%AS3_REBUILD%"=="1" (
    echo Building AS3 Client...
    if exist "%AS3_TEMP_SWF%" del /F /Q "%AS3_TEMP_SWF%"
    powershell -NoProfile -ExecutionPolicy Bypass -File "Skua.AS3\compile-as3.ps1" -OutputPath "%AS3_TEMP_SWF%"
    if errorlevel 1 (
        if exist "%AS3_TEMP_SWF%" del /F /Q "%AS3_TEMP_SWF%"
        echo Failed to build AS3 client. The existing skua.swf was preserved.
        goto :Failure
    )
    if not exist "%AS3_TEMP_SWF%" (
        echo AS3 build completed without producing a temporary SWF. The existing skua.swf was preserved.
        goto :Failure
    )
    move /Y "%AS3_TEMP_SWF%" "Skua.AS3\skua\bin\skua.swf" >nul
    if errorlevel 1 (
        echo Failed to replace skua.swf with the successful build output.
        goto :Failure
    )
) else (
    echo Skipping AS3 build because skua.swf is unchanged.
)

echo Restoring solution once for faster project builds...
dotnet restore "Skua.sln"
if errorlevel 1 goto :Failure

call :BuildProject "Skua.App.Avalonia\Skua.App.Avalonia.csproj" "App" || goto :Failure
call :BuildProject "Skua.Manager.Avalonia\Skua.Manager.Avalonia.csproj" "Manager" || goto :Failure
call :BuildProject "Skua.Plugin.DailyTracker\Skua.Plugin.DailyTracker.csproj" "DailyTracker Plugin" || goto :Failure
call :BuildProject "Skua.Plugin.ButlerV5\Skua.Plugin.ButlerV5.csproj" "ButlerV5 Plugin" || goto :Failure
call :BuildProject "Skua.Plugin.Cosmetics\Skua.Plugin.Cosmetics.csproj" "Cosmetics Plugin" || goto :Failure

echo Packaging and organizing output to match WPF release structure...
powershell -NoProfile -ExecutionPolicy Bypass -File "package-build.ps1"
if errorlevel 1 goto :Failure
if not exist "Build\VibeSkua.exe" (
    echo Build completed without producing Build\VibeSkua.exe.
    goto :Failure
)

echo.
echo ==========================================================
echo Fast Build complete! The application is located in the Build folder.
echo You can run VibeSkua.Manager.exe or VibeSkua.exe from there.
echo ==========================================================
set "BUILD_EXIT_CODE=0"
goto :Finish

:BuildProject
echo Building %~2 (Fast Dev Build)...
dotnet build "%~1" -c Release --no-restore
if errorlevel 1 exit /b 1
exit /b 0

:Failure
set "BUILD_EXIT_CODE=1"
echo.
echo Fast Build failed. Check the output above for details.

:Finish
if defined BUILDPROJECT_PAUSE pause
popd
exit /b %BUILD_EXIT_CODE%

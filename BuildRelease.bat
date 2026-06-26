@echo off
setlocal

rem Read version from Directory.Build.props
for /f "usebackq tokens=3 delims=<>" %%a in (`findstr /i "<Version>" Directory.Build.props`) do set APPVER=%%a

echo Building VibeSkua %APPVER% Release (this will take a moment)...

if exist "Build" rmdir /s /q "Build"
if exist "Releases" rmdir /s /q "Releases"

dotnet build Skua.sln -c Release -p:WarningLevel=0 --nologo
if errorlevel 1 goto :fail

echo =========================================
echo Packaging Velopack Release...
echo =========================================

dotnet tool update -g vpk
if errorlevel 1 goto :fail

vpk pack -u VibeSkua -v %APPVER% -p Build\AnyCPU -e Skua.exe -o Releases
if errorlevel 1 goto :fail

del /Q Releases\*Portable.zip

echo =========================================
echo Build and Packaging Complete (v%APPVER%)!
echo =========================================
pause
exit /b 0

:fail
echo.
echo Build failed with error code %errorlevel%.
pause
exit /b %errorlevel%

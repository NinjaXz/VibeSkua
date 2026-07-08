@echo off
setlocal

rem Read version from Directory.Build.props
for /f "usebackq tokens=3 delims=<>" %%a in (`findstr /i "<Version>" Directory.Build.props`) do set APPVER=%%a

echo Compiling VibeSkua %APPVER% Release (this will take a moment)...

if exist "Build" rmdir /s /q "Build"

dotnet build Skua.sln -c Release -p:WarningLevel=0 --nologo
if errorlevel 1 goto :fail

echo =========================================
echo Compilation Complete (v%APPVER%)!
echo Output files are located in: Build\AnyCPU
echo =========================================
pause
exit /b 0

:fail
echo.
echo Compilation failed with error code %errorlevel%.
pause
exit /b %errorlevel%

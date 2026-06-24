@echo off
echo Building VibeSkua Release (this will take a moment)...

if exist "Build" rmdir /s /q "Build"
if exist "Releases" rmdir /s /q "Releases"

dotnet build Skua.sln -c Release -p:WarningLevel=0 --nologo

echo =========================================
echo Packaging Velopack Release...
echo =========================================

dotnet tool update -g vpk
vpk pack -u VibeSkua -v 1.8.0 -p Build\AnyCPU -e Skua.exe -o Releases
del /Q Releases\*Portable.zip

echo =========================================
echo Build and Packaging Complete!
echo You can find your final installer (Setup.exe)
echo and update files in the /Releases folder!
echo =========================================

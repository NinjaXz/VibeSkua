@echo off
echo Building VibeSkua Release (this will take a moment)...

if exist "Build" rmdir /s /q "Build"

dotnet build Skua.sln -c Release

echo =========================================
echo Build Complete!
echo You can find your build inside
echo the newly created Build folder!
echo =========================================
pause

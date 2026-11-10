# Build-AndroidApp.ps1
# Automates building the Skua Remote Controller Android APK

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$androidDir = Join-Path $scriptDir "Skua.App.Android"

Write-Host "=== Building Skua Android Controller ===" -ForegroundColor Cyan

# Set Android SDK path if available
if (-not $env:ANDROID_HOME) {
    if (Test-Path "C:\Users\Ninja\android-build-env\sdk") {
        $env:ANDROID_HOME = "C:\Users\Ninja\android-build-env\sdk"
    } elseif (Test-Path "$env:LOCALAPPDATA\Android\Sdk") {
        $env:ANDROID_HOME = "$env:LOCALAPPDATA\Android\Sdk"
    }
}

Push-Location $androidDir
try {
    Write-Host "Running Gradle assembleRelease..." -ForegroundColor Yellow
    cmd /c "gradlew.bat assembleRelease --console=plain"
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Gradle build failed with exit code $LASTEXITCODE"
        exit $LASTEXITCODE
    }

    $releaseApk = Join-Path $androidDir "app\build\outputs\apk\release\app-release.apk"
    if (Test-Path $releaseApk) {
        $targetApk = Join-Path $androidDir "skua-remote.apk"
        Copy-Item -Path $releaseApk -Destination $targetApk -Force
        
        $rootApk = Join-Path $scriptDir "skua-remote.apk"
        Copy-Item -Path $releaseApk -Destination $rootApk -Force
        
        # Clean up legacy controller apk names if present
        $oldTargetApk = Join-Path $androidDir "skua-controller.apk"
        if (Test-Path $oldTargetApk) { Remove-Item $oldTargetApk -Force -ErrorAction SilentlyContinue }
        $oldRootApk = Join-Path $scriptDir "skua-controller.apk"
        if (Test-Path $oldRootApk) { Remove-Item $oldRootApk -Force -ErrorAction SilentlyContinue }

        Write-Host "SUCCESS! APK created at: $rootApk" -ForegroundColor Green
        $fileInfo = Get-Item $rootApk
        Write-Host "APK Size: $([math]::Round($fileInfo.Length / 1MB, 2)) MB" -ForegroundColor Green
    }
}
finally {
    Pop-Location
}

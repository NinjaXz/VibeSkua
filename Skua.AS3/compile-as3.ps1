# AS3 Compilation Helper Script
# This script attempts to compile the Skua AS3 client using available compilers

param(
    [switch]$DownloadSDK,
    [string]$SDKPath = "."
)

Write-Host "Skua AS3 Compilation Helper" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan

# Check for ActionScript project
$asProjectPath = "skua\skua.as3proj"
$asconfigPath = "skua\asconfig.json"
$mainSourcePath = "skua\src\skua\Main.as"
$outputPath = "skua\bin\skua.swf"

if (-not (Test-Path $mainSourcePath)) {
    Write-Host "Main.as source file not found: $mainSourcePath" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path "skua\bin")) {
    New-Item -ItemType Directory -Force -Path "skua\bin" | Out-Null
}

Write-Host "Found AS3 source files" -ForegroundColor Green

# Method 1: Check for local Flex SDK or mxmlc in PATH
Write-Host "`nChecking for Flex SDK compiler..." -ForegroundColor Yellow
$localMxmlc = Join-Path $PSScriptRoot "flex_sdk\bin\mxmlc.bat"
$mxmlcCmd = $null

if (Test-Path $localMxmlc) {
    Write-Host "Found local Flex SDK mxmlc at: $localMxmlc" -ForegroundColor Green
    $mxmlcCmd = $localMxmlc
    $localPlayer = Join-Path $PSScriptRoot "flex_sdk\frameworks\libs\player"
    if (Test-Path $localPlayer) {
        $env:PLAYERGLOBAL_HOME = $localPlayer
    }
} else {
    $mxmlcPath = Get-Command mxmlc -ErrorAction SilentlyContinue
    if ($mxmlcPath) {
        Write-Host "Found mxmlc in PATH at: $($mxmlcPath.Source)" -ForegroundColor Green
        $mxmlcCmd = "mxmlc"
    }
}

if ($mxmlcCmd) {
    Write-Host "Compiling with mxmlc..." -ForegroundColor Yellow
    & $mxmlcCmd -source-path "skua\src" -default-size 958 550 -output $outputPath "skua\src\skua\Main.as" -target-player 28.0 -optimize
    
    if ($LASTEXITCODE -eq 0 -and (Test-Path $outputPath)) {
        Write-Host "Compilation successful! Output: $outputPath" -ForegroundColor Green
        Write-Host "SWF size: $((Get-Item $outputPath).Length) bytes" -ForegroundColor Green
        exit 0
    } else {
        Write-Host "Compilation failed with mxmlc" -ForegroundColor Red
    }
}

# Method 2: Check for asconfigc (ActionScript & MXML extension for VS Code)
Write-Host "`nChecking for asconfigc..." -ForegroundColor Yellow
$asconfigcPath = Get-Command asconfigc -ErrorAction SilentlyContinue
if ($asconfigcPath) {
    Write-Host "Found asconfigc at: $($asconfigcPath.Source)" -ForegroundColor Green
    
    if (Test-Path $asconfigPath) {
        Write-Host "Compiling with asconfigc..." -ForegroundColor Yellow
        Push-Location "skua"
        & asconfigc
        Pop-Location
        
        if ($LASTEXITCODE -eq 0 -and (Test-Path $outputPath)) {
            Write-Host "Compilation successful! Output: $outputPath" -ForegroundColor Green
            Write-Host "SWF size: $((Get-Item $outputPath).Length) bytes" -ForegroundColor Green
            exit 0
        } else {
            Write-Host "Compilation failed with asconfigc" -ForegroundColor Red
        }
    }
}

# Method 3: Check for Royale SDK
Write-Host "`nChecking for Apache Royale SDK..." -ForegroundColor Yellow
$royalePath = Get-Command asjsc -ErrorAction SilentlyContinue
if ($royalePath) {
    Write-Host "Found Royale SDK at: $($royalePath.Source)" -ForegroundColor Green
    Write-Host "Note: Royale compiles to HTML/JS, not SWF. Skipping." -ForegroundColor Yellow
}

# Method 4: Check for Adobe Animate
Write-Host "`nChecking for Adobe Animate..." -ForegroundColor Yellow
$animatePath = @(
    "${env:ProgramFiles}\Adobe\Adobe Animate*\Animate.exe",
    "${env:ProgramFiles(x86)}\Adobe\Adobe Animate*\Animate.exe"
) | Get-ChildItem -ErrorAction SilentlyContinue | Select-Object -First 1

if ($animatePath) {
    Write-Host "Found Adobe Animate at: $($animatePath.FullName)" -ForegroundColor Green
    Write-Host "Note: Adobe Animate requires manual compilation. Open the .as3proj file in Animate and publish." -ForegroundColor Yellow
}

if ($DownloadSDK) {
    Write-Host "`nDownloading Flex SDK..." -ForegroundColor Yellow
    Write-Host "Please manually download:" -ForegroundColor Yellow
    Write-Host "   • Apache Flex SDK: https://flex.apache.org/download-binaries.html" -ForegroundColor White
    Write-Host "   • Adobe AIR SDK: https://airsdk.harman.com/download" -ForegroundColor White
}

Write-Host "`nNo ActionScript compiler found or compilation failed!" -ForegroundColor Red
Write-Host "To compile the AS3 code, you need one of:" -ForegroundColor Yellow
Write-Host "   • Adobe Flex SDK with mxmlc compiler" -ForegroundColor White
Write-Host "   • ActionScript & MXML extension for VS Code with asconfigc" -ForegroundColor White
exit 1

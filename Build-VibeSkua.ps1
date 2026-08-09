<#
.SYNOPSIS
    Automated release build script for VibeSkua (Skua + Velopack packaging).
.DESCRIPTION
    Reuses the same Clean / AS3-compile / dotnet-build approach as Build-Skua.ps1,
    but packages the output with Velopack (vpk) instead of a WiX installer.
.EXAMPLE
    .\Build-VibeSkua.ps1
#>

param(
    [switch]$SkipClean
)

$ProgressPreference = "SilentlyContinue"
$ErrorActionPreference = "Stop"

function Write-Header([string]$Message) {
    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host $Message -ForegroundColor Cyan
    Write-Host "========================================`n" -ForegroundColor Cyan
}
function Write-Success([string]$Message) { Write-Host "[SUCCESS] $Message" -ForegroundColor Green }
function Write-BuildError([string]$Message) { Write-Host "[ERROR] $Message" -ForegroundColor Red }
function Write-Info([string]$Message) { Write-Host "[INFO] $Message" -ForegroundColor Yellow }

function Get-AppVersion {
    $propsPath = Join-Path $PSScriptRoot "Directory.Build.props"
    if (-not (Test-Path -LiteralPath $propsPath)) {
        throw "Directory.Build.props not found: $propsPath"
    }
    $content = Get-Content -LiteralPath $propsPath -Raw
    if ($content -match "<Version>([^<]+)</Version>") {
        return $Matches[1].Trim()
    }
    throw "Could not find <Version> element in Directory.Build.props"
}

function Test-Prerequisites {
    Write-Header "Checking Prerequisites"
    $hasErrors = $false

    $dotnetList = dotnet --list-sdks 2>$null
    $hasNet10 = $dotnetList | Where-Object { $_ -match "^10\." }
    if ($hasNet10) {
        $net10Version = ($hasNet10 | Select-Object -First 1) -split ' ' | Select-Object -First 1
        Write-Success ".NET 10 SDK found: $net10Version"
    }
    else {
        Write-BuildError ".NET 10 SDK not found. Install from https://dotnet.microsoft.com/download/dotnet/10.0"
        $hasErrors = $true
    }

    if ($hasErrors) { throw "Prerequisites check failed. Please install missing components." }
    Write-Success "All prerequisites met"
}

function CleanSolution {
    Write-Header "Cleaning Previous Builds"

    @("bin", "obj", "build", "Build", "dist", "publish", "Releases") | Where-Object { Test-Path $_ } | ForEach-Object {
        Write-Info "Removing $_..."
        Remove-Item -Path $_ -Recurse -Force -ErrorAction SilentlyContinue
    }

    Get-ChildItem -Path . -Directory | ForEach-Object {
        $projName = $_.Name
        $projPath = $_.FullName
        @("bin", "obj") | ForEach-Object {
            $targetDir = Join-Path $projPath $_
            if (Test-Path $targetDir) {
                Write-Info "Cleaning $projName\$_..."
                Remove-Item -Path $targetDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }
    Write-Success "Clean completed"
}

function Build-AS3 {
    Write-Header "Building Skua AS3"

    $as3Directory = Join-Path $PSScriptRoot "Skua.AS3"
    $compilerScript = Join-Path $as3Directory "compile-as3.ps1"
    $outputFile = Join-Path $as3Directory "skua\bin\skua.swf"

    $mxmlc = Get-Command mxmlc -ErrorAction SilentlyContinue

    if (-not $mxmlc) {
        $compilerPath = $null

        # First try the standard FLEX_HOME environment variable.
        if (![string]::IsNullOrWhiteSpace($env:FLEX_HOME)) {
            $candidate = Join-Path $env:FLEX_HOME "bin\mxmlc.bat"
            if (Test-Path -LiteralPath $candidate) {
                $compilerPath = $candidate
            }
        }

        # Then look for a Moonshine-installed Flex SDK.
        if (-not $compilerPath) {
            $moonshineRoot = "C:\MoonshineSDKs\Flex_SDK"
            if (Test-Path -LiteralPath $moonshineRoot) {
                $moonshineSdk = Get-ChildItem -LiteralPath $moonshineRoot -Directory |
                    Sort-Object Name -Descending |
                    Where-Object {
                        Test-Path -LiteralPath (Join-Path $_.FullName "bin\mxmlc.bat")
                    } |
                    Select-Object -First 1

                if ($moonshineSdk) {
                    $env:FLEX_HOME = $moonshineSdk.FullName
                    $compilerPath = Join-Path $env:FLEX_HOME "bin\mxmlc.bat"
                }
            }
        }

        if ($compilerPath) {
            $compilerDirectory = Split-Path $compilerPath -Parent
            $env:PATH = "$compilerDirectory;$env:PATH"
            $mxmlc = Get-Command mxmlc -ErrorAction SilentlyContinue
        }
    }

    if (-not $mxmlc) {
        throw "No ActionScript compiler was found. Install a Flex SDK or configure FLEX_HOME."
    }

    Write-Info "Using ActionScript compiler: $($mxmlc.Source)"

    # Only remove the old output after confirming that a compiler exists.
    if (Test-Path -LiteralPath $outputFile) {
        Remove-Item -LiteralPath $outputFile -Force
    }

    Push-Location $as3Directory
    try {
        $result = & pwsh -NoProfile -ExecutionPolicy Bypass -File $compilerScript 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if ($exitCode -ne 0 -or !(Test-Path -LiteralPath $outputFile)) {
        Write-Host $result -ForegroundColor Red
        throw "AS3 compilation failed. Refusing to package a stale or missing skua.swf."
    }

    $swf = Get-Item -LiteralPath $outputFile
    Write-Success "AS3 compiled successfully: $($swf.Length) bytes"
}

function Build-Solution {
    Write-Header "Building Skua.sln (Release)"
    $result = dotnet build "Skua.sln" -c Release -p:WarningLevel=0 --nologo 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-BuildError "Build failed"
        Write-Host $result -ForegroundColor Red
        throw "Build failed"
    }
    Write-Success "Build completed"
}

function Publish-VelopackRelease([string]$Version) {
    Write-Header "Packaging Velopack Release"

    Write-Info "Updating vpk tool..."
    $result = dotnet tool update -g vpk 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-BuildError "vpk tool update failed"
        Write-Host $result -ForegroundColor Red
        throw "vpk tool update failed"
    }

    Write-Info "Packing VibeSkua v$Version..."
    $result = vpk pack -u VibeSkua -v $Version -p "Build\AnyCPU" -e "Skua.exe" -o "Releases" 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-BuildError "vpk pack failed"
        Write-Host $result -ForegroundColor Red
        throw "vpk pack failed"
    }

    Get-ChildItem -Path "Releases" -Filter "*Portable.zip" -ErrorAction SilentlyContinue | Remove-Item -Force

    Write-Success "Velopack packaging completed"
}

function Show-Summary([TimeSpan]$TotalTime, [bool]$Success, [string]$Version) {
    Write-Header "Build Summary"
    if ($Success) { Write-Success "Build completed successfully! (v$Version)" } else { Write-BuildError "Build completed with errors" }
    Write-Info "Total time: $($TotalTime.TotalSeconds.ToString('F2'))s"

    if ($Success -and (Test-Path "Releases")) {
        Write-Host "`nOutput: $(Resolve-Path 'Releases')" -ForegroundColor Yellow
        Get-ChildItem -Path "Releases" -Recurse -File | ForEach-Object {
            Write-Host "  - $($_.Name)" -ForegroundColor Gray
        }
    }
}

function Wait-ForKeyPress([int]$ExitCode = 0) {
    Write-Host "`n========================================" -ForegroundColor DarkGray
    Write-Host $(if ($ExitCode -eq 0) { "Press any key to exit..." } else { "Build failed. Press any key to exit..." }) -ForegroundColor $(if ($ExitCode -eq 0) { "Green" } else { "Red" })
    Write-Host "========================================" -ForegroundColor DarkGray
    try { $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyUp") } catch { }
    exit $ExitCode
}

function Main {
    Push-Location $PSScriptRoot
    $totalStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $success = $false
    $exitCode = 0
    $version = "unknown"

    $dotnetPath = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
    if ($dotnetPath) {
        $dotnetDir = Split-Path $dotnetPath -Parent
        $env:PATH = "$dotnetDir;$env:PATH"
    }

    try {
        Write-Header "VibeSkua Release Build"
        $version = Get-AppVersion
        Write-Info "Version: $version"

        Test-Prerequisites
        if (-not $SkipClean) { CleanSolution }

        Build-AS3
        Build-Solution
        Publish-VelopackRelease -Version $version

        $success = $true
    }
    catch {
        Write-BuildError "Build failed: $_"
        $exitCode = 1
    }
    finally {
        $totalStopwatch.Stop()
        Show-Summary -TotalTime $totalStopwatch.Elapsed -Success $success -Version $version
        Pop-Location
        Wait-ForKeyPress -ExitCode $exitCode
    }
}

Main

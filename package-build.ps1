param(
    [string]$RootDir = $PSScriptRoot
)

$ErrorActionPreference = "Stop"

$AppBin = Join-Path $RootDir "Skua.App.Avalonia\bin\Release\net10.0-windows"
$ManagerBin = Join-Path $RootDir "Skua.Manager.Avalonia\bin\Release\net10.0"
$CoreDir = Join-Path $RootDir "Skua.Core"
$SkillsDir = Join-Path $CoreDir "Skills"
$FFDecDir = Join-Path $RootDir "FFDec"
$SwfSource = Join-Path $RootDir "Skua.AS3\skua\bin\skua.swf"
$As3BuildScript = Join-Path $RootDir "Skua.AS3\compile-as3.ps1"

if (-not (Test-Path -LiteralPath $As3BuildScript -PathType Leaf)) {
    throw "The AS3 compiler script is missing: $As3BuildScript"
}

# Compile once during packaging. BuildProject.bat deliberately delegates AS3 work here
# so the packaged client always contains the output from the current ActionScript sources.
$SwfDirectory = Split-Path -Parent $SwfSource
$SwfBuildTemp = Join-Path $SwfDirectory "skua.build-$PID-$([guid]::NewGuid().ToString('N')).tmp.swf"

try {
    Write-Host "Compiling ActionScript sources into a temporary SWF..." -ForegroundColor Cyan
    & $As3BuildScript -OutputPath $SwfBuildTemp

    if (-not (Test-Path -LiteralPath $SwfBuildTemp -PathType Leaf)) {
        throw "The AS3 compiler completed without producing the expected SWF: $SwfBuildTemp"
    }

    # Replace the previous SWF only after compilation succeeds.
    Move-Item -LiteralPath $SwfBuildTemp -Destination $SwfSource -Force
}
finally {
    if (Test-Path -LiteralPath $SwfBuildTemp -PathType Leaf) {
        Remove-Item -LiteralPath $SwfBuildTemp -Force
    }
}

if (-not (Test-Path -LiteralPath $SwfSource -PathType Leaf)) {
    throw "The compiled ActionScript client is missing: $SwfSource"
}

$SwfStream = [System.IO.File]::OpenRead($SwfSource)
try {
    $SwfHeader = New-Object byte[] 3
    [void]$SwfStream.Read($SwfHeader, 0, $SwfHeader.Length)
}
finally {
    $SwfStream.Dispose()
}

$SwfSignature = [System.Text.Encoding]::ASCII.GetString($SwfHeader)
if ($SwfSignature -notin @('FWS', 'CWS', 'ZWS')) {
    throw "The compiled ActionScript client is not a valid SWF (signature: $SwfSignature): $SwfSource"
}

Write-Host "Using freshly compiled ActionScript client: $SwfSource" -ForegroundColor Green

if (-not (Test-Path -LiteralPath $AppBin -PathType Container)) {
    throw "The application build output is missing. Build Skua.sln before packaging: $AppBin"
}

Copy-Item -LiteralPath $SwfSource -Destination (Join-Path $AppBin "skua.swf") -Force

$LegacyAnyCPU = Join-Path $RootDir "Build\AnyCPU"
if (Test-Path $LegacyAnyCPU) {
    Remove-Item $LegacyAnyCPU -Recurse -Force -ErrorAction SilentlyContinue
}

$Targets = @(
    (Join-Path $RootDir "Build")
)
$LocalCurrent = Join-Path $env:LOCALAPPDATA "VibeSkua\current"
if (Test-Path $LocalCurrent) {
    $Targets += $LocalCurrent
}

# Stage a private Java 21 runtime beside FFDec so end users never need a system Java installation.
if (-not (Test-Path (Join-Path $FFDecDir "ffdec.jar"))) {
    throw "The bundled FFDec JAR is missing: $(Join-Path $FFDecDir 'ffdec.jar')"
}

$BundledJavaRuntime = Join-Path $RootDir "Build\FFDec\Runtime"
$BundledJavaExecutable = Join-Path $BundledJavaRuntime "bin\java.exe"
if (-not (Test-Path -LiteralPath $BundledJavaExecutable -PathType Leaf)) {
    $RuntimeBuilder = Join-Path $RootDir "Build-FFDecRuntime.ps1"
    if (-not (Test-Path -LiteralPath $RuntimeBuilder -PathType Leaf)) {
        throw "The FFDec runtime setup script is missing: $RuntimeBuilder"
    }

    & $RuntimeBuilder -DestinationDirectory $BundledJavaRuntime
    if (-not (Test-Path -LiteralPath $BundledJavaExecutable -PathType Leaf)) {
        throw "The portable Java runtime was not staged successfully: $BundledJavaExecutable"
    }
}

foreach ($Target in $Targets) {
    if (-not (Test-Path $Target)) {
        New-Item -ItemType Directory -Path $Target -Force | Out-Null
    }

    $AssembliesDir = Join-Path $Target "Assemblies"
    $TargetSkillsDir = Join-Path $AssembliesDir "Skills"
    $TargetFFDecDir = Join-Path $Target "FFDec"
    $PluginsDir = Join-Path $Target "plugins"

    New-Item -ItemType Directory -Path $AssembliesDir -Force | Out-Null
    New-Item -ItemType Directory -Path $TargetSkillsDir -Force | Out-Null
    New-Item -ItemType Directory -Path $TargetFFDecDir -Force | Out-Null
    New-Item -ItemType Directory -Path $PluginsDir -Force | Out-Null

    # 1. Copy Root Files from AppBin
    $RootAppFiles = @(
        "VibeSkua.exe", "VibeSkua.dll", "VibeSkua.runtimeconfig.json", "VibeSkua.deps.json",
        "QuestData.json", "AdvancedSkills.json", "ScriptDates.json"
    )
    foreach ($file in $RootAppFiles) {
        $src = Join-Path $AppBin $file
        if (Test-Path $src) {
            try {
                Copy-Item $src (Join-Path $Target $file) -Force -ErrorAction Stop
            } catch {
                Write-Warning "File in use, skipped copying $file to $Target"
            }
        } elseif ($file -eq "ScriptDates.json" -and (Test-Path (Join-Path $CoreDir "ScriptDates.json"))) {
            Copy-Item (Join-Path $CoreDir "ScriptDates.json") (Join-Path $Target $file) -Force
        }
    }

    # 2. Copy Root Files from ManagerBin
    $RootManagerFiles = @("VibeSkua.Manager.exe", "VibeSkua.Manager.dll", "VibeSkua.Manager.runtimeconfig.json", "Velopack.dll")
    foreach ($file in $RootManagerFiles) {
        $src = Join-Path $ManagerBin $file
        if (Test-Path $src) {
            try {
                Copy-Item $src (Join-Path $Target $file) -Force -ErrorAction Stop
            } catch {
                Write-Warning "File in use, skipped copying $file to $Target"
            }
        }
    }

    # 3. Copy the freshly compiled ActionScript client.
    Copy-Item -LiteralPath $SwfSource -Destination (Join-Path $Target "skua.swf") -Force

    # 4. Copy all DLLs and XMLs into Assemblies (excluding root DLLs)
    $ExcludedDlls = @("VibeSkua.dll", "VibeSkua.Manager.dll", "Skua.dll", "Skua.Manager.dll")
    $ManagerDlls = Get-ChildItem -Path $ManagerBin -Filter "*.dll" -File
    foreach ($dll in $ManagerDlls) {
        if ($ExcludedDlls -notcontains $dll.Name) {
            Copy-Item $dll.FullName (Join-Path $AssembliesDir $dll.Name) -Force
        }
    }

    $AppDlls = Get-ChildItem -Path $AppBin -Filter "*.dll" -File
    foreach ($dll in $AppDlls) {
        if ($ExcludedDlls -notcontains $dll.Name) {
            Copy-Item $dll.FullName (Join-Path $AssembliesDir $dll.Name) -Force
        }
    }

    $XmlDocs = Get-ChildItem -Path $AppBin -Filter "*.xml" -File
    foreach ($xml in $XmlDocs) {
        Copy-Item $xml.FullName (Join-Path $AssembliesDir $xml.Name) -Force
    }

    # 5. Copy Windows x64 and x86 runtimes to Assemblies (skip Mac, Linux, and ARM)
    $RuntimesSrc = Join-Path $AppBin "runtimes"
    if (Test-Path $RuntimesSrc) {
        $TargetRuntimes = Join-Path $AssembliesDir "runtimes"
        if (Test-Path $TargetRuntimes) {
            Remove-Item $TargetRuntimes -Recurse -Force -ErrorAction SilentlyContinue
        }
        New-Item -ItemType Directory -Path $TargetRuntimes -Force | Out-Null

        $AllowedRuntimes = @("win-x64", "win-x86")
        foreach ($runtime in $AllowedRuntimes) {
            $src = Join-Path $RuntimesSrc $runtime
            if (Test-Path $src) {
                Copy-Item $src (Join-Path $TargetRuntimes $runtime) -Recurse -Force
            }
        }
    }

    # 6. Copy Skills to Assemblies\Skills
    if (Test-Path $SkillsDir) {
        Get-ChildItem -Path $SkillsDir -Filter "*.cs" -File | ForEach-Object {
            Copy-Item $_.FullName (Join-Path $TargetSkillsDir $_.Name) -Force
        }
    }

    # 7. Copy FFDec and its private Java runtime.
    if (Test-Path $FFDecDir) {
        Copy-Item (Join-Path $FFDecDir "*") $TargetFFDecDir -Recurse -Force
    }

    $TargetJavaRuntime = Join-Path $TargetFFDecDir "Runtime"
    $TargetJavaExecutable = Join-Path $TargetJavaRuntime "bin\java.exe"
    if (-not (Test-Path -LiteralPath $TargetJavaExecutable -PathType Leaf)) {
        New-Item -ItemType Directory -Path $TargetJavaRuntime -Force | Out-Null
        Get-ChildItem -LiteralPath $BundledJavaRuntime -Force |
            Copy-Item -Destination $TargetJavaRuntime -Recurse -Force
    }

    if (-not (Test-Path -LiteralPath $TargetJavaExecutable -PathType Leaf)) {
        throw "The packaged FFDec Java runtime is missing: $TargetJavaExecutable"
    }

    # 8. Copy Plugins
    $PluginSourceDir = Join-Path $RootDir "Skua.Plugin.DailyTracker\bin\Release\net10.0"
    if (Test-Path $PluginSourceDir) {
        Get-ChildItem -Path $PluginSourceDir -Filter "Skua.Plugin.*.dll" -File | ForEach-Object {
            Copy-Item $_.FullName (Join-Path $PluginsDir $_.Name) -Force
        }
    }
    $ButlerSourceDir = Join-Path $RootDir "Skua.Plugin.ButlerV5\bin\Release\net10.0"
    if (Test-Path $ButlerSourceDir) {
        $butlerDll = Join-Path $ButlerSourceDir "ButlerV5.dll"
        if (Test-Path $butlerDll) {
            Copy-Item $butlerDll (Join-Path $PluginsDir "ButlerV5.dll") -Force
        }
    }
    $CosmeticsSourceDir = Join-Path $RootDir "Skua.Plugin.Cosmetics\bin\Release\net10.0"
    if (Test-Path $CosmeticsSourceDir) {
        $cosmeticsDll = Join-Path $CosmeticsSourceDir "Skua.Plugin.Cosmetics.dll"
        if (Test-Path $cosmeticsDll) {
            Copy-Item $cosmeticsDll (Join-Path $PluginsDir "Skua.Plugin.Cosmetics.dll") -Force
        }
    }

    # 9. Ensure OMNISCOPE is built and copy the staged DLL into the client package.
    $OmniscopeDll = Join-Path $RootDir "Build\plugins\Skua.Plugin.Omniscope.dll"
    if (-not (Test-Path $OmniscopeDll)) {
        $OmniscopeProject = Join-Path $RootDir "Skua.Plugin.Omniscope\Skua.Plugin.Omniscope.csproj"
        Write-Host "OMNISCOPE output is missing. Building the plugin before packaging..."
        & dotnet build $OmniscopeProject --configuration Release --verbosity minimal
        if ($LASTEXITCODE -ne 0) {
            throw "OMNISCOPE build failed with exit code $LASTEXITCODE."
        }
    }

    if (-not (Test-Path $OmniscopeDll)) {
        throw "OMNISCOPE build completed without staging the expected DLL: $OmniscopeDll"
    }

    $OmniscopeTargetDll = Join-Path $PluginsDir "Skua.Plugin.Omniscope.dll"
    if ([System.IO.Path]::GetFullPath($OmniscopeDll) -ne [System.IO.Path]::GetFullPath($OmniscopeTargetDll)) {
        Copy-Item $OmniscopeDll $OmniscopeTargetDll -Force
    }

    # 10. Clean up stray files from target root
    $AllowedRootFiles = @(
        "VibeSkua.exe", "VibeSkua.dll", "VibeSkua.runtimeconfig.json", "VibeSkua.deps.json",
        "VibeSkua.Manager.exe", "VibeSkua.Manager.dll", "VibeSkua.Manager.runtimeconfig.json", "VibeSkua.Manager.deps.json",
        "Velopack.dll", "skua.swf", "AdvancedSkills.json", "QuestData.json", "ScriptDates.json"
    )

    Get-ChildItem -Path $Target -File | Where-Object {
        $AllowedRootFiles -notcontains $_.Name
    } | ForEach-Object {
        Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue
    }

    # Clean culture directories and extra runtimes from target root
    $CultureDirs = @("cs", "de", "es", "fr", "it", "ja", "ko", "pl", "pt-BR", "ru", "tr", "zh-Hans", "zh-Hant", "runtimes")
    foreach ($cd in $CultureDirs) {
        $rootCd = Join-Path $Target $cd
        if (Test-Path $rootCd) { Remove-Item $rootCd -Recurse -Force -ErrorAction SilentlyContinue }
    }

    # Clean culture directories from Assemblies
    foreach ($cd in $CultureDirs) {
        if ($cd -ne "runtimes") {
            $asmCd = Join-Path $AssembliesDir $cd
            if (Test-Path $asmCd) { Remove-Item $asmCd -Recurse -Force -ErrorAction SilentlyContinue }
        }
    }
}

Write-Host "Packaging complete for Build."

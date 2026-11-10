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

    # 3. Copy skua.swf
    if (Test-Path $SwfSource) {
        Copy-Item $SwfSource (Join-Path $Target "skua.swf") -Force
    } elseif (Test-Path (Join-Path $AppBin "skua.swf")) {
        Copy-Item (Join-Path $AppBin "skua.swf") (Join-Path $Target "skua.swf") -Force
    }

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

    # 7. Copy FFDec
    if (Test-Path $FFDecDir) {
        Copy-Item (Join-Path $FFDecDir "*") $TargetFFDecDir -Recurse -Force
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

    # 9. Clean up stray files from target root
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

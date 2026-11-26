[CmdletBinding()]
param(
    [string]$MxmlcPath,
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Join-Path $scriptRoot 'skua'
$sourcePath = Join-Path $projectRoot 'src'
$mainSourcePath = Join-Path $sourcePath 'skua\Main.as'
$outputDirectory = Join-Path $projectRoot 'bin'
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $outputPath = Join-Path $outputDirectory 'skua.swf'
}
else {
    $outputPath = [System.IO.Path]::GetFullPath($OutputPath)
}

if (-not (Test-Path -LiteralPath $mainSourcePath)) {
    throw "AS3 entry point was not found: $mainSourcePath"
}

if ([string]::IsNullOrWhiteSpace($MxmlcPath)) {
    $mxmlcCommand = Get-Command mxmlc -ErrorAction SilentlyContinue
    if ($mxmlcCommand) {
        $MxmlcPath = $mxmlcCommand.Source
    }
    elseif ($env:FLEX_HOME) {
        $candidate = Join-Path $env:FLEX_HOME 'bin\mxmlc.bat'
        if (Test-Path -LiteralPath $candidate) {
            $MxmlcPath = $candidate
        }
    }
}

if ([string]::IsNullOrWhiteSpace($MxmlcPath)) {
    $searchRoots = @(
        'C:\MoonshineSDKs',
        'C:\ApacheFlex',
        $env:ProgramFiles,
        ${env:ProgramFiles(x86)},
        $env:LOCALAPPDATA
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path -LiteralPath $_) } | Select-Object -Unique

    foreach ($searchRoot in $searchRoots) {
        $foundCompiler = Get-ChildItem -LiteralPath $searchRoot -Filter 'mxmlc.bat' -File -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($foundCompiler) {
            $MxmlcPath = $foundCompiler.FullName
            break
        }
    }
}

if ([string]::IsNullOrWhiteSpace($MxmlcPath) -or -not (Test-Path -LiteralPath $MxmlcPath)) {
    throw 'Flex mxmlc was not found. Add it to PATH, set FLEX_HOME, install a Flex SDK in a common SDK location, or pass -MxmlcPath.'
}

if (-not $env:FLEX_HOME) {
    $mxmlcDirectory = Split-Path -Parent (Resolve-Path -LiteralPath $MxmlcPath).Path
    $env:FLEX_HOME = Split-Path -Parent $mxmlcDirectory
}
if ([string]::IsNullOrWhiteSpace($env:PLAYERGLOBAL_HOME)) {
    $env:PLAYERGLOBAL_HOME = Join-Path $env:FLEX_HOME 'frameworks\libs\player'
}

$playerGlobalSource = Join-Path $scriptRoot 'playerglobal\28.0\playerglobal.swc'
if (-not (Test-Path -LiteralPath $playerGlobalSource -PathType Leaf)) {
    throw "PlayerGlobal 28 is missing from the repository: $playerGlobalSource"
}

$playerGlobalDirectory = Join-Path $env:PLAYERGLOBAL_HOME '28.0'
$playerGlobalTarget = Join-Path $playerGlobalDirectory 'playerglobal.swc'
New-Item -ItemType Directory -Path $playerGlobalDirectory -Force | Out-Null

$sourceHash = (Get-FileHash -LiteralPath $playerGlobalSource -Algorithm SHA256).Hash
$targetHash = if (Test-Path -LiteralPath $playerGlobalTarget -PathType Leaf) {
    (Get-FileHash -LiteralPath $playerGlobalTarget -Algorithm SHA256).Hash
}
else {
    $null
}

if ($targetHash -ne $sourceHash) {
    Copy-Item -LiteralPath $playerGlobalSource -Destination $playerGlobalTarget -Force
    Write-Host 'Installed repository PlayerGlobal 28 into the Flex SDK.' -ForegroundColor DarkCyan
}

$resolvedOutputDirectory = Split-Path -Parent $outputPath
New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null

& $MxmlcPath `
    -source-path $sourcePath `
    -default-size 958 550 `
    -output $outputPath `
    $mainSourcePath `
    -target-player 28.0 `
    -optimize

if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $outputPath)) {
    throw "AS3 compilation failed with exit code ${LASTEXITCODE}: $outputPath"
}

$output = Get-Item -LiteralPath $outputPath
if ($output.Length -le 8) {
    throw "AS3 compiler produced an invalid or incomplete SWF: $outputPath"
}

$stream = [System.IO.File]::OpenRead($outputPath)
try {
    $headerBytes = New-Object byte[] 3
    [void]$stream.Read($headerBytes, 0, $headerBytes.Length)
}
finally {
    $stream.Dispose()
}

$signature = [System.Text.Encoding]::ASCII.GetString($headerBytes)
if ($signature -notin @('FWS', 'CWS', 'ZWS')) {
    throw "AS3 compiler output does not have a valid SWF signature: $signature"
}

Write-Host "AS3 game content built: $($output.FullName) ($($output.Length) bytes, $signature)" -ForegroundColor Green

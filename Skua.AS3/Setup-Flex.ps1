[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$flexHome = $null
if (-not [string]::IsNullOrWhiteSpace($env:FLEX_HOME)) {
    $candidate = Join-Path $env:FLEX_HOME 'bin\mxmlc.bat'
    if (Test-Path -LiteralPath $candidate -PathType Leaf) {
        $flexHome = (Resolve-Path -LiteralPath $env:FLEX_HOME).Path
    }
}

if ($null -eq $flexHome) {
    $mxmlcCommand = Get-Command 'mxmlc' -ErrorAction SilentlyContinue
    if ($null -ne $mxmlcCommand -and $mxmlcCommand.Source -like '*.bat') {
        $flexHome = Split-Path -Parent (Split-Path -Parent $mxmlcCommand.Source)
    }
}

if ($null -eq $flexHome) {
    $searchRoots = @(
        'C:\MoonshineSDKs',
        'C:\ApacheFlex'
    ) | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path -LiteralPath $_)
    } | Select-Object -Unique

    foreach ($searchRoot in $searchRoots) {
        $compiler = Get-ChildItem -LiteralPath $searchRoot -Filter 'mxmlc.bat' -File -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -ne $compiler) {
            $flexHome = Split-Path -Parent $compiler.DirectoryName
            break
        }
    }
}

if ($null -eq $flexHome) {
    if (-not (Get-Command 'java' -ErrorAction SilentlyContinue)) {
        throw 'Java was not found. Install a Java 8 JDK before setting up Apache Flex.'
    }

    $downloadDirectory = if (-not [string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) {
        Join-Path $env:RUNNER_TEMP 'VibeSkua-Flex'
    }
    else {
        Join-Path $env:TEMP 'VibeSkua-Flex'
    }

    $archivePath = Join-Path $downloadDirectory 'apache-flex-sdk-4.16.1-bin.zip'
    $extractPath = Join-Path $downloadDirectory 'sdk'
    $downloadUrl = 'https://dlcdn.apache.org/flex/4.16.1/binaries/apache-flex-sdk-4.16.1-bin.zip'
    $expectedChecksum = '8841c64bd5e32f8575eba86e2574873a'

    New-Item -ItemType Directory -Path $downloadDirectory -Force | Out-Null

    if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
        Write-Host 'Downloading Apache Flex SDK 4.16.1...'
        Invoke-WebRequest -Uri $downloadUrl -OutFile $archivePath
    }

    $actualChecksum = (Get-FileHash -LiteralPath $archivePath -Algorithm MD5).Hash.ToLowerInvariant()
    if ($actualChecksum -ne $expectedChecksum) {
        Remove-Item -LiteralPath $archivePath -Force -ErrorAction SilentlyContinue
        throw "Apache Flex SDK checksum mismatch. Expected $expectedChecksum, received $actualChecksum."
    }

    if (-not (Test-Path -LiteralPath $extractPath -PathType Container)) {
        New-Item -ItemType Directory -Path $extractPath -Force | Out-Null
        Write-Host 'Extracting Apache Flex SDK...'
        Expand-Archive -LiteralPath $archivePath -DestinationPath $extractPath -Force
    }

    $compiler = Get-ChildItem -LiteralPath $extractPath -Filter 'mxmlc.bat' -File -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $compiler) {
        throw "Apache Flex SDK was extracted, but mxmlc.bat was not found under $extractPath."
    }

    $flexHome = Split-Path -Parent $compiler.DirectoryName
}

$mxmlcPath = Join-Path $flexHome 'bin\mxmlc.bat'
if (-not (Test-Path -LiteralPath $mxmlcPath -PathType Leaf)) {
    throw "Apache Flex compiler was not found: $mxmlcPath"
}

$playerGlobalHome = Join-Path $flexHome 'frameworks\libs\player'
$env:FLEX_HOME = $flexHome
$env:PLAYERGLOBAL_HOME = $playerGlobalHome
$env:PATH = "$(Join-Path $flexHome 'bin');$env:PATH"

if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_ENV)) {
    Add-Content -LiteralPath $env:GITHUB_ENV -Value "FLEX_HOME=$flexHome"
    Add-Content -LiteralPath $env:GITHUB_ENV -Value "PLAYERGLOBAL_HOME=$playerGlobalHome"
}

if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_PATH)) {
    Add-Content -LiteralPath $env:GITHUB_PATH -Value (Join-Path $flexHome 'bin')
}

Write-Host "Apache Flex SDK: $flexHome" -ForegroundColor Green
Write-Host "PlayerGlobal directory: $playerGlobalHome"

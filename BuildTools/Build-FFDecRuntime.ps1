[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$DestinationDirectory
)

$ErrorActionPreference = "Stop"

$JavaMajorVersion = 21
$MetadataUri = "https://api.adoptium.net/v3/assets/latest/$JavaMajorVersion/hotspot?architecture=x64&image_type=jre&os=windows&vendor=eclipse"
$DestinationDirectory = [System.IO.Path]::GetFullPath($DestinationDirectory)
$BundledJava = Join-Path $DestinationDirectory "bin\java.exe"

if (Test-Path -LiteralPath $BundledJava -PathType Leaf) {
    Write-Host "Bundled Java runtime already exists: $BundledJava"
    exit 0
}

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$TemporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "VibeSkua-FFDec-Java-$([Guid]::NewGuid().ToString('N'))"
$ArchivePath = Join-Path $TemporaryDirectory "temurin-jre.zip"
$ExtractionDirectory = Join-Path $TemporaryDirectory "Extracted"

try {
    New-Item -ItemType Directory -Path $TemporaryDirectory -Force | Out-Null

    Write-Host "Resolving the latest Eclipse Temurin Java $JavaMajorVersion x64 JRE package..."
    $Assets = @(Invoke-RestMethod -Uri $MetadataUri -Method Get -Headers @{ Accept = "application/json" } -TimeoutSec 60)
    $Asset = $Assets |
        Where-Object {
            $_.binary.package.name -match "\.zip$" -and
            $_.binary.package.link -match "^https://" -and
            $_.binary.package.checksum -match "^[a-fA-F0-9]{64}$"
        } |
        Select-Object -First 1

    if ($null -eq $Asset) {
        throw "Adoptium did not return a Java $JavaMajorVersion Windows x64 JRE ZIP package with a SHA-256 checksum. Metadata endpoint: $MetadataUri"
    }

    $Package = $Asset.binary.package
    $ExpectedHash = [string]$Package.checksum
    Write-Host "Downloading $($Package.name)..."
    Invoke-WebRequest -Uri $Package.link -OutFile $ArchivePath -UseBasicParsing -TimeoutSec 900

    $ActualHash = (Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash
    if (-not [string]::Equals($ActualHash, $ExpectedHash, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Java runtime checksum verification failed. Expected $ExpectedHash, received $ActualHash."
    }

    Write-Host "Java runtime archive SHA-256 verified. Extracting..."
    Expand-Archive -LiteralPath $ArchivePath -DestinationPath $ExtractionDirectory -Force

    $JavaExecutable = Get-ChildItem -LiteralPath $ExtractionDirectory -Filter "java.exe" -File -Recurse |
        Where-Object { $_.Directory.Name -eq "bin" } |
        Select-Object -First 1

    if ($null -eq $JavaExecutable) {
        throw "The verified Temurin archive did not contain bin\java.exe."
    }

    $JavaHome = Split-Path -Parent (Split-Path -Parent $JavaExecutable.FullName)
    $VersionStdOutPath = Join-Path $TemporaryDirectory "java-version.stdout.txt"
    $VersionStdErrPath = Join-Path $TemporaryDirectory "java-version.stderr.txt"
    $JavaVersionProcess = Start-Process -FilePath $JavaExecutable.FullName -ArgumentList "-version" -WorkingDirectory $JavaHome -NoNewWindow -Wait -PassThru -RedirectStandardOutput $VersionStdOutPath -RedirectStandardError $VersionStdErrPath
    $VersionOutput = @(
        if (Test-Path -LiteralPath $VersionStdOutPath) { Get-Content -LiteralPath $VersionStdOutPath -Raw }
        if (Test-Path -LiteralPath $VersionStdErrPath) { Get-Content -LiteralPath $VersionStdErrPath -Raw }
    ) -join [Environment]::NewLine
    $VersionOutput = $VersionOutput.Trim()

    if ($JavaVersionProcess.ExitCode -ne 0 -or $VersionOutput -notmatch 'version "21(?:\.|")') {
        throw "The bundled Java runtime failed its version check (exit code $($JavaVersionProcess.ExitCode)). Output: $VersionOutput"
    }

    if (Test-Path -LiteralPath $DestinationDirectory) {
        Remove-Item -LiteralPath $DestinationDirectory -Recurse -Force
    }

    New-Item -ItemType Directory -Path $DestinationDirectory -Force | Out-Null
    Get-ChildItem -LiteralPath $JavaHome -Force |
        Copy-Item -Destination $DestinationDirectory -Recurse -Force

    if (-not (Test-Path -LiteralPath $BundledJava -PathType Leaf)) {
        throw "Java extraction completed without staging the expected runtime: $BundledJava"
    }

    @"
Distribution: Eclipse Temurin JRE
Java major version: $JavaMajorVersion
Package: $($Package.name)
SHA-256: $ActualHash
Download: $($Package.link)
"@ | Set-Content -LiteralPath (Join-Path $DestinationDirectory "VibeSkua-Runtime-Info.txt") -Encoding UTF8

    Write-Host "Bundled Java runtime ready: $BundledJava"
    Write-Host $VersionOutput
}
finally {
    if (Test-Path -LiteralPath $TemporaryDirectory) {
        Remove-Item -LiteralPath $TemporaryDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}

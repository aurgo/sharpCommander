#Requires -Version 5.1
<#
.SYNOPSIS
    SharpCommander - publish script for Windows (Windows PowerShell 5.1 or PowerShell 7).
.DESCRIPTION
    A thin wrapper over 'dotnet publish'. The publish configuration (self-contained, single-file, partial trimming, no debug
    symbols) lives in src\SharpCommander.Desktop\SharpCommander.Desktop.csproj so every platform and every script
    ships the same binaries, and the version is read from Directory.Build.props.
    ZIP archives are created by default. macOS platforms are published as loose files here; run publish.sh on
    macOS (or Linux) to get the SharpCommander.app bundle.
.PARAMETER Platform
    One or more runtime identifiers: win-x64, win-x86, win-arm64, linux-x64, linux-arm64, osx-x64, osx-arm64,
    or all (default).
.PARAMETER AOT
    Native AOT compilation (-p:PublishAot=true). Needs the platform's native toolchain; no cross-OS builds.
.PARAMETER NoZip
    Skip creating ZIP archives.
.EXAMPLE
    .\publish.ps1
    .\publish.ps1 -Platform win-x64
    .\publish.ps1 -Platform win-x64,win-arm64 -NoZip
    .\publish.ps1 -Platform win-x64 -AOT
#>

param(
    [Parameter()]
    [ValidateSet("win-x64", "win-x86", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64", "all")]
    [string[]]$Platform = @("all"),

    [Parameter()]
    [switch]$AOT,

    [Parameter()]
    [switch]$NoZip
)

$ErrorActionPreference = "Stop"
# PowerShell 7.4+ would otherwise turn a failing 'dotnet publish' into a terminating error before the exit
# code is inspected; the script reports the failure itself and continues with the next platform.
$PSNativeCommandUseErrorActionPreference = $false

# Configuration (paths are relative to the repository root, where this script lives)
$ProjectPath = Join-Path "src" (Join-Path "SharpCommander.Desktop" "SharpCommander.Desktop.csproj")
$PropsFile = "Directory.Build.props"
$OutputBase = "publish"
$AppName = "SharpCommander"
$AllPlatforms = @("win-x64", "win-x86", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64")
$PlatformNames = @{
    "win-x64"     = "Windows x64"
    "win-x86"     = "Windows x86"
    "win-arm64"   = "Windows ARM64"
    "linux-x64"   = "Linux x64"
    "linux-arm64" = "Linux ARM64"
    "osx-x64"     = "macOS x64 (Intel)"
    "osx-arm64"   = "macOS ARM64 (Apple Silicon)"
}

function Write-Header {
    Write-Host ""
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host "  SharpCommander - Build and Publish Script" -ForegroundColor Cyan
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host ""
}

# Reads the <Version> element of Directory.Build.props, the single source of the version.
function Get-ProjectVersion {
    $match = Select-String -Path $PropsFile -Pattern '<Version>\s*([^<\s]+)\s*</Version>' | Select-Object -First 1
    if (-not $match) {
        throw "No <Version> element found in $PropsFile"
    }

    return $match.Matches[0].Groups[1].Value
}

function Publish-Platform {
    param(
        [string]$RuntimeId
    )

    $outputDir = Join-Path $OutputBase $RuntimeId

    Write-Host "[Building for $($PlatformNames[$RuntimeId])...]" -ForegroundColor Yellow

    # Start from a clean folder so files of an earlier publish never end up in the archive.
    if (Test-Path $outputDir) {
        Remove-Item $outputDir -Recurse -Force
    }

    # The csproj supplies the publish settings; only configuration, runtime and output are passed here.
    $publishArgs = @(
        "publish"
        $ProjectPath
        "-c", "Release"
        "-r", $RuntimeId
        "-o", $outputDir
    )

    if ($AOT) {
        $publishArgs += "-p:PublishAot=true"
        Write-Host "  AOT compilation enabled" -ForegroundColor Magenta
    }

    # Out-Host keeps the build output out of the function's return value.
    & dotnet @publishArgs | Out-Host

    if ($LASTEXITCODE -ne 0) {
        Write-Host "  ERROR: Build failed for $RuntimeId" -ForegroundColor Red
        return $false
    }

    $files = @(Get-ChildItem $outputDir -Recurse -File)
    $totalSize = [math]::Round(($files | Measure-Object -Property Length -Sum).Sum / 1MB, 2)
    Write-Host "  SUCCESS: $outputDir ($($files.Count) files, $totalSize MB)" -ForegroundColor Green

    if (-not $NoZip) {
        $zipName = "$AppName-v$Version-$RuntimeId.zip"
        $zipPath = Join-Path $OutputBase $zipName

        Write-Host "  Creating ZIP: $zipName" -ForegroundColor Cyan

        if (Test-Path $zipPath) {
            Remove-Item $zipPath -Force
        }

        Compress-Archive -Path (Join-Path $outputDir "*") -DestinationPath $zipPath -Force

        $zipSize = [math]::Round((Get-Item $zipPath).Length / 1MB, 2)
        Write-Host "  ZIP created: $zipPath ($zipSize MB)" -ForegroundColor Green
    }

    return $true
}

# Main execution
$exitCode = 0
Push-Location $PSScriptRoot
try {
    Write-Header

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw "The .NET SDK ('dotnet') was not found on the PATH."
    }

    $Version = Get-ProjectVersion
    Write-Host "Version $Version (from $PropsFile)" -ForegroundColor Cyan

    # Create output directory
    if (-not (Test-Path $OutputBase)) {
        New-Item -ItemType Directory -Path $OutputBase | Out-Null
    }

    $targetPlatforms = if ($Platform -contains "all") { $AllPlatforms } else { @($Platform | Select-Object -Unique) }
    $successful = @()
    $failed = @()

    foreach ($rid in $targetPlatforms) {
        Write-Host ""
        if (Publish-Platform -RuntimeId $rid) {
            $successful += $rid
        } else {
            $failed += $rid
        }
    }

    # Summary
    Write-Host ""
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host "  Build Summary" -ForegroundColor Cyan
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host ""

    if ($successful.Count -gt 0) {
        Write-Host "Successful builds ($($successful.Count)):" -ForegroundColor Green
        foreach ($rid in $successful) {
            Write-Host "  - $rid" -ForegroundColor Green
        }
    }

    if ($failed.Count -gt 0) {
        Write-Host ""
        Write-Host "Failed builds ($($failed.Count)):" -ForegroundColor Red
        foreach ($rid in $failed) {
            Write-Host "  - $rid" -ForegroundColor Red
        }
        $exitCode = 1
    }

    Write-Host ""
    Write-Host "Output directory: $OutputBase" -ForegroundColor Cyan

    if (-not $NoZip) {
        Write-Host ""
        Write-Host "ZIP files created:" -ForegroundColor Cyan
        Get-ChildItem $OutputBase -Filter "*.zip" | ForEach-Object {
            $size = [math]::Round($_.Length / 1MB, 2)
            Write-Host "  $($_.Name) ($size MB)" -ForegroundColor White
        }
    }

    Write-Host ""
    if ($exitCode -eq 0) {
        Write-Host "Done!" -ForegroundColor Green
    } else {
        Write-Host "Done with errors." -ForegroundColor Red
    }
}
finally {
    Pop-Location
}

exit $exitCode

#Requires -Version 5.1
<#
.SYNOPSIS
    SharpCommander - Cross-platform publish script
.DESCRIPTION
    Publishes SharpCommander for Windows, Linux, and macOS platforms.
    By default, creates ZIP archives for distribution.
.PARAMETER Platform
    Target platform: win-x64, win-x86, win-arm64, linux-x64, linux-arm64, osx-x64, osx-arm64, all
.PARAMETER AOT
    Enable AOT compilation (requires Visual Studio C++ tools on Windows)
.PARAMETER NoZip
    Skip creating ZIP archives
.EXAMPLE
    .\publish.ps1 -Platform all
    .\publish.ps1 -Platform win-x64
    .\publish.ps1 -Platform linux-x64 -AOT
    .\publish.ps1 -Platform all -NoZip
#>

param(
    [Parameter()]
    [ValidateSet("win-x64", "win-x86", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64", "all")]
    [string]$Platform = "all",
    
    [Parameter()]
    [switch]$AOT,
    
    [Parameter()]
    [switch]$NoZip
)

$ErrorActionPreference = "Stop"

# Configuration
$ProjectPath = "src\SharpCommander.Desktop\SharpCommander.Desktop.csproj"
$OutputBase = "publish"
$Version = "2.0.0"

# Platform definitions
$Platforms = @{
    "win-x64"     = @{ Name = "Windows x64"; Extension = ".exe" }
    "win-x86"     = @{ Name = "Windows x86"; Extension = ".exe" }
    "win-arm64"   = @{ Name = "Windows ARM64"; Extension = ".exe" }
    "linux-x64"   = @{ Name = "Linux x64"; Extension = "" }
    "linux-arm64" = @{ Name = "Linux ARM64"; Extension = "" }
    "osx-x64"     = @{ Name = "macOS x64 (Intel)"; Extension = "" }
    "osx-arm64"   = @{ Name = "macOS ARM64 (Apple Silicon)"; Extension = "" }
}

function Write-Header {
    Write-Host ""
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host "  SharpCommander - Build and Publish Script" -ForegroundColor Cyan
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host ""
}

function Publish-Platform {
    param(
        [string]$RuntimeId
    )
    
    $platformInfo = $Platforms[$RuntimeId]
    $outputDir = Join-Path $OutputBase $RuntimeId
    
    Write-Host "[Building for $($platformInfo.Name)...]" -ForegroundColor Yellow
    
    # Self-contained with partial trimming for smaller size
    # PublishSingleFile is NOT used because Avalonia requires native DLLs
    $publishArgs = @(
        "publish"
        $ProjectPath
        "-c", "Release"
        "-r", $RuntimeId
        "--self-contained", "true"
        "-p:PublishTrimmed=true"
        "-p:TrimMode=partial"
        "-o", $outputDir
    )
    
    if ($AOT) {
        $publishArgs += "-p:PublishAot=true"
        Write-Host "  AOT compilation enabled" -ForegroundColor Magenta
    }
    
    & dotnet @publishArgs
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  ERROR: Build failed for $RuntimeId" -ForegroundColor Red
        return $false
    }
    
    # Get folder size
    $files = Get-ChildItem $outputDir
    $totalSize = [math]::Round(($files | Measure-Object -Property Length -Sum).Sum / 1MB, 2)
    Write-Host "  SUCCESS: $outputDir ($($files.Count) files, $totalSize MB)" -ForegroundColor Green
    
    # Create ZIP by default (unless -NoZip is specified)
    if (-not $NoZip) {
        $zipName = "SharpCommander-v$Version-$RuntimeId.zip"
        $zipPath = Join-Path $OutputBase $zipName
        
        Write-Host "  Creating ZIP: $zipName" -ForegroundColor Cyan
        
        if (Test-Path $zipPath) {
            Remove-Item $zipPath -Force
        }
        
        Compress-Archive -Path "$outputDir\*" -DestinationPath $zipPath -Force
        
        $zipSize = [math]::Round((Get-Item $zipPath).Length / 1MB, 2)
        Write-Host "  ZIP created: $zipPath ($zipSize MB)" -ForegroundColor Green
    }
    
    return $true
}

# Main execution
Write-Header

# Create output directory
if (-not (Test-Path $OutputBase)) {
    New-Item -ItemType Directory -Path $OutputBase | Out-Null
}

$targetPlatforms = if ($Platform -eq "all") { $Platforms.Keys } else { @($Platform) }
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
Write-Host "Done!" -ForegroundColor Green

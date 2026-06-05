$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$DownloadsPath = Join-Path $ProjectRoot "hosted-ui\downloads"
$WindowsPackage = Join-Path $DownloadsPath "corporate-rag-windows.zip"
$MacPackage = Join-Path $DownloadsPath "corporate-rag-macos.zip"
$StagingRoot = Join-Path $ProjectRoot ".setup\packages"

function Copy-Project($target) {
    if (Test-Path $target) {
        Remove-Item -Recurse -Force $target
    }

    New-Item -ItemType Directory -Force -Path $target | Out-Null

    Get-ChildItem $ProjectRoot -Force |
        Where-Object {
            $_.Name -notin @("bin", "obj", "data", "uploads", ".setup", ".git", "hosted-ui")
        } |
        ForEach-Object {
            Copy-Item $_.FullName -Destination $target -Recurse -Force
        }
}

New-Item -ItemType Directory -Force -Path $DownloadsPath | Out-Null

$windowsStaging = Join-Path $StagingRoot "windows"
$macStaging = Join-Path $StagingRoot "macos"

Copy-Project $windowsStaging
Copy-Project $macStaging

if (Test-Path $WindowsPackage) {
    Remove-Item $WindowsPackage -Force
}

if (Test-Path $MacPackage) {
    Remove-Item $MacPackage -Force
}

Compress-Archive -Path (Join-Path $windowsStaging "*") -DestinationPath $WindowsPackage -Force
Compress-Archive -Path (Join-Path $macStaging "*") -DestinationPath $MacPackage -Force

Write-Host "Created:"
Write-Host "  $WindowsPackage"
Write-Host "  $MacPackage"

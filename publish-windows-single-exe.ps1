$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$PublishRoot = Join-Path $ProjectRoot "publish"
$PublishDir = Join-Path $PublishRoot "windows-x64-single"
$HostedDownloads = Join-Path $ProjectRoot "hosted-ui\downloads"
$HostedExePath = Join-Path $HostedDownloads "CorporateRag-win-x64.exe"

Set-Location $ProjectRoot

if (Test-Path $PublishDir) {
    Remove-Item -Recurse -Force $PublishDir
}

New-Item -ItemType Directory -Force -Path $PublishDir | Out-Null
New-Item -ItemType Directory -Force -Path $HostedDownloads | Out-Null

dotnet publish `
    .\CorporateRag.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$exePath = Join-Path $PublishDir "CorporateRag.exe"
if (-not (Test-Path $exePath)) {
    throw "Single-file executable was not created: $exePath"
}

Copy-Item $exePath -Destination $HostedExePath -Force

Write-Host "Created single-file executable:"
Write-Host "  $exePath"
Write-Host ""
Write-Host "Copied to hosted downloads:"
Write-Host "  $HostedExePath"

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$PublishRoot = Join-Path $ProjectRoot "publish"
$PublishDir = Join-Path $PublishRoot "windows-x64"
$ZipPath = Join-Path $PublishRoot "corporate-rag-windows-x64.zip"
$HostedDownloads = Join-Path $ProjectRoot "hosted-ui\downloads"
$HostedZipPath = Join-Path $HostedDownloads "corporate-rag-windows-x64.zip"

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
    -p:PublishSingleFile=false `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Copy-Item .\start-rag-fast.bat -Destination $PublishDir -Force
Copy-Item .\start-rag-quality.bat -Destination $PublishDir -Force
Copy-Item .\start-rag.bat -Destination $PublishDir -Force
Copy-Item .\start-rag.ps1 -Destination $PublishDir -Force
Copy-Item .\README.md -Destination $PublishDir -Force

if (Test-Path $ZipPath) {
    Remove-Item $ZipPath -Force
}

Compress-Archive -Path (Join-Path $PublishDir "*") -DestinationPath $ZipPath -Force
Copy-Item $ZipPath -Destination $HostedZipPath -Force

Write-Host "Created publish folder:"
Write-Host "  $PublishDir"
Write-Host ""
Write-Host "Main executable:"
Write-Host "  $(Join-Path $PublishDir 'CorporateRag.exe')"
Write-Host ""
Write-Host "Created archives:"
Write-Host "  $ZipPath"
Write-Host "  $HostedZipPath"

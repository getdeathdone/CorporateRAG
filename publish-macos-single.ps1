$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$PublishRoot = Join-Path $ProjectRoot "publish"
$HostedDownloads = Join-Path $ProjectRoot "hosted-ui\downloads"

Set-Location $ProjectRoot
New-Item -ItemType Directory -Force -Path $HostedDownloads | Out-Null

function Publish-MacRuntime($runtime) {
    $publishDir = Join-Path $PublishRoot $runtime
    $archivePath = Join-Path $PublishRoot "corporate-rag-$runtime.zip"
    $hostedArchivePath = Join-Path $HostedDownloads "corporate-rag-$runtime.zip"

    if (Test-Path $publishDir) {
        Remove-Item -Recurse -Force $publishDir
    }

    New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

    dotnet publish `
        .\CorporateRag.csproj `
        -c Release `
        -r $runtime `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:IncludeAllContentForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -o $publishDir

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $runtime with exit code $LASTEXITCODE"
    }

    Copy-Item .\start-rag-mac.sh -Destination $publishDir -Force
    Copy-Item .\install.command -Destination $publishDir -Force
    Copy-Item .\README.md -Destination $publishDir -Force

    if (Test-Path $archivePath) {
        Remove-Item $archivePath -Force
    }

    Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $archivePath -Force
    Copy-Item $archivePath -Destination $hostedArchivePath -Force

    Write-Host "Created macOS package:"
    Write-Host "  $archivePath"
    Write-Host "  $hostedArchivePath"
}

Publish-MacRuntime "osx-arm64"
Publish-MacRuntime "osx-x64"

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$PublishRoot = Join-Path $ProjectRoot "publish"
$HostedDownloads = Join-Path $ProjectRoot "hosted-ui\downloads"

Set-Location $ProjectRoot
New-Item -ItemType Directory -Force -Path $HostedDownloads | Out-Null

function New-ZipFromDirectory($sourceDirectory, $archivePath) {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    if (Test-Path $archivePath) {
        Remove-Item $archivePath -Force
    }

    $sourceFullPath = [System.IO.Path]::GetFullPath($sourceDirectory).TrimEnd('\', '/')
    $zip = [System.IO.Compression.ZipFile]::Open($archivePath, [System.IO.Compression.ZipArchiveMode]::Create)

    try {
        Get-ChildItem -Path $sourceFullPath -Recurse -File | ForEach-Object {
            $fileFullPath = [System.IO.Path]::GetFullPath($_.FullName)
            $relativePath = $fileFullPath.Substring($sourceFullPath.Length).TrimStart('\', '/')
            $entryName = $relativePath -replace '\\', '/'
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip,
                $fileFullPath,
                $entryName,
                [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    finally {
        $zip.Dispose()
    }
}

function Convert-TextFileToLf($path) {
    if (-not (Test-Path $path)) {
        return
    }

    $text = [System.IO.File]::ReadAllText($path)
    $text = $text -replace "`r`n", "`n"
    $text = $text -replace "`r", "`n"
    [System.IO.File]::WriteAllText($path, $text, [System.Text.UTF8Encoding]::new($false))
}

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
    Convert-TextFileToLf (Join-Path $publishDir "start-rag-mac.sh")
    Convert-TextFileToLf (Join-Path $publishDir "install.command")

    New-ZipFromDirectory $publishDir $archivePath
    Copy-Item $archivePath -Destination $hostedArchivePath -Force

    Write-Host "Created macOS package:"
    Write-Host "  $archivePath"
    Write-Host "  $hostedArchivePath"
}

Publish-MacRuntime "osx-arm64"
Publish-MacRuntime "osx-x64"

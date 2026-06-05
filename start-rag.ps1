param(
    [ValidateSet("fast", "quality")]
    [string]$Mode = "fast"
)

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$WebUrl = "http://localhost:5000"
$OllamaEndpoint = "http://localhost:11434"
$ChatModel = if ($Mode -eq "quality") { "llama3.1:8b" } else { "llama3.2:3b" }
$EmbeddingModel = "nomic-embed-text:latest"
$DownloadsPath = Join-Path $ProjectRoot ".setup"
$DotnetSdkInstallerUrl = "https://aka.ms/dotnet/8.0/dotnet-sdk-win-x64.exe"
$OllamaInstallerUrl = "https://ollama.com/download/OllamaSetup.exe"

function Find-DotnetExe {
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $programFilesPath = Join-Path $env:ProgramFiles "dotnet\dotnet.exe"
    if (Test-Path $programFilesPath) {
        return $programFilesPath
    }

    return $null
}

function Find-WingetExe {
    $command = Get-Command winget -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    return $null
}

function Write-Step($message) {
    Write-Host ""
    Write-Host "==> $message" -ForegroundColor Cyan
}

function Write-Ok($message) {
    Write-Host "OK  $message" -ForegroundColor Green
}

function Write-Warn($message) {
    Write-Host "!!  $message" -ForegroundColor Yellow
}

function Download-File($url, $outputPath) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $outputPath) | Out-Null
    Write-Host "Downloading $url"

    $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
    if ($curl) {
        & $curl.Source -L --fail --retry 3 --output $outputPath $url
        if ($LASTEXITCODE -eq 0 -and (Test-Path $outputPath)) {
            return
        }
    }

    try {
        Import-Module BitsTransfer -ErrorAction Stop
        Start-BitsTransfer -Source $url -Destination $outputPath -ErrorAction Stop
        if (Test-Path $outputPath) {
            return
        }
    }
    catch {
        Write-Warn "BITS download failed, falling back to PowerShell web request."
    }

    $previousProgressPreference = $ProgressPreference
    try {
        $ProgressPreference = "SilentlyContinue"
        Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $outputPath
    }
    finally {
        $ProgressPreference = $previousProgressPreference
    }
}

function Find-OllamaExe {
    $command = Get-Command ollama -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $localAppDataPath = Join-Path $env:LOCALAPPDATA "Programs\Ollama\ollama.exe"
    if (Test-Path $localAppDataPath) {
        return $localAppDataPath
    }

    $programFilesPath = Join-Path $env:ProgramFiles "Ollama\ollama.exe"
    if (Test-Path $programFilesPath) {
        return $programFilesPath
    }

    return $null
}

function Test-HttpOk($url) {
    try {
        Invoke-WebRequest -UseBasicParsing $url -TimeoutSec 3 | Out-Null
        return $true
    }
    catch {
        return $false
    }
}

function Wait-ForHttp($url, $seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-HttpOk $url) {
            return $true
        }

        Start-Sleep -Milliseconds 500
    }

    return $false
}

function Get-OllamaModels {
    try {
        $response = Invoke-WebRequest -UseBasicParsing "$OllamaEndpoint/api/tags" -TimeoutSec 5
        return (($response.Content | ConvertFrom-Json).models | Select-Object -ExpandProperty name)
    }
    catch {
        return @()
    }
}

function Test-ModelInstalled($models, $modelName) {
    foreach ($model in $models) {
        if ($model -ieq $modelName) {
            return $true
        }

        if ($model -ieq "$modelName`:latest") {
            return $true
        }

        if ("$model`:latest" -ieq $modelName) {
            return $true
        }
    }

    return $false
}

Set-Location $ProjectRoot
$wingetExe = Find-WingetExe

Write-Step "Selected startup mode"
Write-Ok "Mode: $Mode"
Write-Ok "Chat model: $ChatModel"

Write-Step "Checking .NET SDK"
$dotnetVersion = ""
$dotnetExe = Find-DotnetExe
try {
    if ($dotnetExe) {
        $dotnetVersion = & $dotnetExe --version 2>$null
    }
}
catch {
    $dotnetVersion = ""
}

if ([string]::IsNullOrWhiteSpace($dotnetVersion)) {
    Write-Warn ".NET 8 SDK is not installed or is not visible in PATH."

    if ($wingetExe) {
        Write-Step "Installing .NET 8 SDK with winget"
        & $wingetExe install --id Microsoft.DotNet.SDK.8 --exact --accept-package-agreements --accept-source-agreements
        $dotnetExe = Find-DotnetExe
        if ($dotnetExe) {
            $dotnetVersion = & $dotnetExe --version 2>$null
        }
    }
    else {
        Write-Step "winget was not found. Downloading .NET 8 SDK installer directly"
        $dotnetInstaller = Join-Path $DownloadsPath "dotnet-sdk-win-x64.exe"
        Download-File $DotnetSdkInstallerUrl $dotnetInstaller
        Write-Step "Installing .NET 8 SDK"
        Start-Process -FilePath $dotnetInstaller -ArgumentList @("/install", "/quiet", "/norestart") -Wait
        $dotnetExe = Find-DotnetExe
        if ($dotnetExe) {
            $dotnetVersion = & $dotnetExe --version 2>$null
        }
    }
}

if ([string]::IsNullOrWhiteSpace($dotnetVersion)) {
    Write-Warn ".NET 8 SDK installation did not finish successfully."
    Write-Host "Install it from: https://dotnet.microsoft.com/download/dotnet/8.0"
    Start-Process "https://dotnet.microsoft.com/download/dotnet/8.0" | Out-Null
    exit 1
}

Write-Ok "Using .NET SDK $dotnetVersion"

Write-Step "Checking Ollama"
$ollamaExe = Find-OllamaExe
if (-not $ollamaExe) {
    Write-Warn "Ollama is not installed or is not visible in PATH."

    if ($wingetExe) {
        Write-Step "Installing Ollama with winget"
        & $wingetExe install --id Ollama.Ollama --exact --accept-package-agreements --accept-source-agreements
        $ollamaExe = Find-OllamaExe
    }
    else {
        Write-Step "winget was not found. Downloading Ollama installer directly"
        $ollamaInstaller = Join-Path $DownloadsPath "OllamaSetup.exe"
        Download-File $OllamaInstallerUrl $ollamaInstaller
        Write-Step "Installing Ollama"
        Start-Process -FilePath $ollamaInstaller -ArgumentList @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART") -Wait
        $ollamaExe = Find-OllamaExe
    }
}

if (-not $ollamaExe) {
    Write-Warn "Ollama installation did not finish successfully."
    Write-Host "Install it from: https://ollama.com/download/windows"
    Start-Process "https://ollama.com/download/windows" | Out-Null
    exit 1
}

Write-Ok "Found Ollama: $ollamaExe"

if (-not (Test-HttpOk "$OllamaEndpoint/api/tags")) {
    Write-Step "Starting Ollama"
    Start-Process -FilePath $ollamaExe -ArgumentList @("serve") -WindowStyle Hidden | Out-Null

    if (-not (Wait-ForHttp "$OllamaEndpoint/api/tags" 20)) {
        Write-Warn "Ollama did not start at $OllamaEndpoint."
        Write-Host "Try opening Ollama manually, then run this file again."
        exit 1
    }
}

Write-Ok "Ollama is reachable"

Write-Step "Checking local models"
$models = @(Get-OllamaModels)

if (-not (Test-ModelInstalled $models $ChatModel)) {
    Write-Warn "Missing chat model: $ChatModel"
    & $ollamaExe pull $ChatModel
}
else {
    Write-Ok "Chat model installed: $ChatModel"
}

$models = @(Get-OllamaModels)
if (-not (Test-ModelInstalled $models $EmbeddingModel)) {
    Write-Warn "Missing embedding model: $EmbeddingModel"
    & $ollamaExe pull $EmbeddingModel
}
else {
    Write-Ok "Embedding model installed: $EmbeddingModel"
}

Write-Step "Restoring and building project"
Get-Process CorporateRag -ErrorAction SilentlyContinue | Stop-Process -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

& $dotnetExe restore
& $dotnetExe build --no-restore

Write-Step "Starting Corporate RAG"
Start-Process -FilePath $dotnetExe -ArgumentList @("run", "--no-build", "--urls", $WebUrl) -WorkingDirectory $ProjectRoot -WindowStyle Hidden | Out-Null

if (-not (Wait-ForHttp "$WebUrl/api/status" 20)) {
    Write-Warn "The web app did not start on $WebUrl."
    Write-Host "Run manually: dotnet run --urls $WebUrl"
    exit 1
}

Write-Ok "Corporate RAG is running"
Write-Host ""
Write-Host "Opening $WebUrl"
Start-Process $WebUrl | Out-Null

Write-Host ""
Write-Host "Done. You can close this window. The web app keeps running in the background."

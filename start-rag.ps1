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
$OllamaInstallScriptUrl = "https://ollama.com/install.ps1"
$TotalStopwatch = [System.Diagnostics.Stopwatch]::StartNew()

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

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Restart-AsAdministrator {
    Write-Warn "Administrator rights are required to install missing dependencies."
    Write-Step "Restarting setup as administrator"
    $argumentList = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", "`"$PSCommandPath`"",
        "-Mode", $Mode
    )

    Start-Process -FilePath "powershell.exe" -ArgumentList $argumentList -Verb RunAs -WorkingDirectory $ProjectRoot
    exit 0
}

function Invoke-WingetInstall($packageId, $name) {
    & $wingetExe install `
        --id $packageId `
        --exact `
        --silent `
        --disable-interactivity `
        --accept-package-agreements `
        --accept-source-agreements

    if ($LASTEXITCODE -ne 0) {
        throw "$name winget install failed with exit code $LASTEXITCODE"
    }
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

function Format-Duration([TimeSpan]$duration) {
    if ($duration.TotalHours -ge 1) {
        return "{0}h {1}m {2}s" -f [int]$duration.TotalHours, $duration.Minutes, $duration.Seconds
    }

    if ($duration.TotalMinutes -ge 1) {
        return "{0}m {1}s" -f [int]$duration.TotalMinutes, $duration.Seconds
    }

    return "{0}s" -f [Math]::Max(0, [int]$duration.TotalSeconds)
}

function Start-Timer($label) {
    Write-Step $label
    return [System.Diagnostics.Stopwatch]::StartNew()
}

function Stop-Timer($label, [System.Diagnostics.Stopwatch]$timer) {
    $timer.Stop()
    Write-Ok "$label took $(Format-Duration $timer.Elapsed)"
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

function Invoke-OllamaPull($ollamaPath, $modelName) {
    $command = "`"$ollamaPath`" pull $modelName"
    cmd.exe /d /c $command

    if ($LASTEXITCODE -ne 0) {
        throw "ollama pull failed for $modelName"
    }
}

Set-Location $ProjectRoot
$wingetExe = Find-WingetExe
$isAdministrator = Test-IsAdministrator

Write-Step "Selected startup mode"
Write-Ok "Mode: $Mode"
Write-Ok "Chat model: $ChatModel"
if ($Mode -eq "quality") {
    Write-Warn "Estimated first setup time: 10-45 minutes depending on model download speed."
}
else {
    Write-Warn "Estimated first setup time: 5-20 minutes depending on model download speed."
}

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

    if (-not $isAdministrator) {
        Restart-AsAdministrator
    }

    if ($wingetExe) {
        $timer = Start-Timer "Installing .NET 8 SDK with winget"
        Invoke-WingetInstall "Microsoft.DotNet.SDK.8" ".NET SDK"
        Stop-Timer ".NET SDK installation" $timer
        $dotnetExe = Find-DotnetExe
        if ($dotnetExe) {
            $dotnetVersion = & $dotnetExe --version 2>$null
        }
    }
    else {
        $timer = Start-Timer "winget was not found. Downloading .NET 8 SDK installer directly"
        $dotnetInstaller = Join-Path $DownloadsPath "dotnet-sdk-win-x64.exe"
        Download-File $DotnetSdkInstallerUrl $dotnetInstaller
        Stop-Timer ".NET SDK download" $timer
        $timer = Start-Timer "Installing .NET 8 SDK"
        Start-Process -FilePath $dotnetInstaller -ArgumentList @("/install", "/quiet", "/norestart") -Wait
        Stop-Timer ".NET SDK installation" $timer
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

    if (-not $isAdministrator) {
        Restart-AsAdministrator
    }

    if ($wingetExe) {
        $timer = Start-Timer "Installing Ollama with winget"
        try {
            Invoke-WingetInstall "Ollama.Ollama" "Ollama"
            Stop-Timer "Ollama winget installation" $timer
        }
        catch {
            $timer.Stop()
            Write-Warn "winget install failed."
        }

        $ollamaExe = Find-OllamaExe
    }

    if (-not $ollamaExe) {
        $timer = Start-Timer "Installing Ollama with the official PowerShell script"
        try {
            Invoke-RestMethod $OllamaInstallScriptUrl | Invoke-Expression
            Stop-Timer "Ollama PowerShell installation" $timer
            $ollamaExe = Find-OllamaExe
        }
        catch {
            $timer.Stop()
            Write-Warn "Official PowerShell install script failed. Downloading Ollama installer directly."
        }
    }

    if (-not $ollamaExe) {
        try {
            $timer = Start-Timer "Downloading Ollama installer"
            $ollamaInstaller = Join-Path $DownloadsPath "OllamaSetup.exe"
            Download-File $OllamaInstallerUrl $ollamaInstaller
            Stop-Timer "Ollama installer download" $timer
            $timer = Start-Timer "Installing Ollama"
            Start-Process -FilePath $ollamaInstaller -ArgumentList @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART") -Wait
            Stop-Timer "Ollama installer run" $timer
            $ollamaExe = Find-OllamaExe
        }
        catch {
            Write-Warn "Direct Ollama installer failed."
        }
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
    $timer = Start-Timer "Downloading chat model $ChatModel"
    Invoke-OllamaPull $ollamaExe $ChatModel
    Stop-Timer "Chat model download" $timer
}
else {
    Write-Ok "Chat model installed: $ChatModel"
}

$models = @(Get-OllamaModels)
if (-not (Test-ModelInstalled $models $EmbeddingModel)) {
    Write-Warn "Missing embedding model: $EmbeddingModel"
    $timer = Start-Timer "Downloading embedding model $EmbeddingModel"
    Invoke-OllamaPull $ollamaExe $EmbeddingModel
    Stop-Timer "Embedding model download" $timer
}
else {
    Write-Ok "Embedding model installed: $EmbeddingModel"
}

Write-Step "Restoring and building project"
Get-Process CorporateRag -ErrorAction SilentlyContinue | Stop-Process -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

$timer = Start-Timer "Restoring NuGet packages"
& $dotnetExe restore
Stop-Timer "NuGet restore" $timer

$timer = Start-Timer "Building project"
& $dotnetExe build --no-restore
Stop-Timer "Project build" $timer

Write-Step "Starting Corporate RAG"
Start-Process -FilePath $dotnetExe -ArgumentList @("run", "--no-build", "--urls", $WebUrl, "--", "--no-open") -WorkingDirectory $ProjectRoot -WindowStyle Hidden | Out-Null

if (-not (Wait-ForHttp "$WebUrl/api/status" 20)) {
    Write-Warn "The web app did not start on $WebUrl."
    Write-Host "Run manually: dotnet run --urls $WebUrl"
    exit 1
}

Write-Ok "Corporate RAG is running"
Write-Ok "Total startup time: $(Format-Duration $TotalStopwatch.Elapsed)"
Write-Host ""
Write-Host "Opening $WebUrl"
Start-Process $WebUrl | Out-Null

Write-Host ""
Write-Host "Done. You can close this window. The web app keeps running in the background."

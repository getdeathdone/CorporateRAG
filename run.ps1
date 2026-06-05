$ErrorActionPreference = "Stop"

Write-Host "Checking .NET SDK..."
$dotnetVersion = ""
try {
    $dotnetVersion = & dotnet --version 2>$null
}
catch {
    $dotnetVersion = ""
}

if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($dotnetVersion)) {
    Write-Host ""
    Write-Host ".NET SDK is not installed or is not visible in PATH."
    Write-Host "Install .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0"
    exit 1
}

Write-Host "Using .NET SDK $dotnetVersion"
Write-Host ""
Write-Host "For local Ollama mode, make sure these models exist:"
Write-Host "  ollama pull llama3.2:3b"
Write-Host "  ollama pull nomic-embed-text"
Write-Host ""

dotnet restore
dotnet run --urls http://localhost:5000

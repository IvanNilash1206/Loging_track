# Install-Agent.ps1
# Run as Administrator to install the LogSystem Agent as a Windows Service
# Usage: .\scripts\Install-Agent.ps1 [-ApiEndpoint "https://your-server:5001"] [-ApiKey "your-key"]

param(
    [string]$ApiEndpoint = "https://localhost:5001",
    [string]$ApiKey = "CHANGE_ME_TO_A_SECURE_KEY",
    [string]$InstallPath = "C:\Program Files\LogSystem\Agent",
    [string]$ServiceName = "LogSystemAgent"
)

$ErrorActionPreference = "Stop"

# Check admin
$currentUser = [Security.Principal.WindowsPrincipal]([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentUser.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "This script must be run as Administrator."
    exit 1
}

Write-Host "=== LogSystem Agent Installer ===" -ForegroundColor Cyan

# Build the agent
Write-Host "Building agent..." -ForegroundColor Yellow
$projectPath = Join-Path $PSScriptRoot "..\src\LogSystem.Agent\LogSystem.Agent.csproj"
dotnet publish $projectPath -c Release -r win-x64 --self-contained -o "$InstallPath" /p:PublishSingleFile=true

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed."
    exit 1
}

# Update configuration
$configPath = Join-Path $InstallPath "appsettings.json"
$config = Get-Content $configPath | ConvertFrom-Json
$config.AgentConfiguration.ApiEndpoint = $ApiEndpoint
$config.AgentConfiguration.ApiKey = $ApiKey
$config.AgentConfiguration.DeviceId = "$($env:COMPUTERNAME)-$($env:USERNAME)".ToUpper()
$config | ConvertTo-Json -Depth 10 | Set-Content $configPath
Write-Host "Configuration updated." -ForegroundColor Green

# Create queue directory
$queuePath = $config.AgentConfiguration.Security.LocalQueuePath
if (-not (Test-Path $queuePath)) {
    New-Item -ItemType Directory -Path $queuePath -Force | Out-Null
    Write-Host "Created queue directory: $queuePath" -ForegroundColor Green
}

# Install as Windows Service
$exePath = Join-Path $InstallPath "LogSystem.Agent.exe"

$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService) {
    Write-Host "Stopping existing service..." -ForegroundColor Yellow
    Stop-Service -Name $ServiceName -Force
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

Write-Host "Installing Windows Service..." -ForegroundColor Yellow
sc.exe create $ServiceName binPath= "`"$exePath`"" start= auto DisplayName= "LogSystem Agent"
sc.exe description $ServiceName "LogSystem endpoint monitoring agent"
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/30000

# Start the service
Start-Service -Name $ServiceName
Write-Host ""
Write-Host "=== LogSystem Agent installed and running ===" -ForegroundColor Green
Write-Host "Service Name : $ServiceName"
Write-Host "Install Path : $InstallPath"
Write-Host "API Endpoint : $ApiEndpoint"
Write-Host "Device ID    : $($config.AgentConfiguration.DeviceId)"

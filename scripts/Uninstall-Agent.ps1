# Uninstall-Agent.ps1
# Run as Administrator to uninstall the LogSystem Agent Windows Service

param(
    [string]$ServiceName = "LogSystemAgent",
    [string]$InstallPath = "C:\Program Files\LogSystem\Agent"
)

$ErrorActionPreference = "Stop"

$currentUser = [Security.Principal.WindowsPrincipal]([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentUser.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "This script must be run as Administrator."
    exit 1
}

Write-Host "=== LogSystem Agent Uninstaller ===" -ForegroundColor Cyan

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($service) {
    Write-Host "Stopping service..." -ForegroundColor Yellow
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Write-Host "Service removed." -ForegroundColor Green
} else {
    Write-Host "Service not found." -ForegroundColor Yellow
}

if (Test-Path $InstallPath) {
    Write-Host "Removing files from $InstallPath..." -ForegroundColor Yellow
    Remove-Item -Path $InstallPath -Recurse -Force
    Write-Host "Files removed." -ForegroundColor Green
}

Write-Host ""
Write-Host "=== LogSystem Agent uninstalled ===" -ForegroundColor Green
Write-Host "Note: Queue data in C:\ProgramData\LogSystem was preserved."
Write-Host "Delete manually if no longer needed."

# Start-Dashboard.ps1
# Starts the LogSystem Dashboard API + Web UI for development

param(
    [string]$Port = "5001",
    [string]$ApiKey = "CHANGE_ME_TO_A_SECURE_KEY",
    [string]$FirebaseProjectId = "",
    [string]$FirebaseCredentialPath = ""
)

$projectPath = Join-Path $PSScriptRoot "..\src\LogSystem.Dashboard\LogSystem.Dashboard.csproj"
$dashboardDir = Join-Path $PSScriptRoot "..\src\LogSystem.Dashboard"

# Validate Firebase credential file
if ($FirebaseCredentialPath -and !(Test-Path $FirebaseCredentialPath)) {
    Write-Error "Firebase credential file not found: $FirebaseCredentialPath"
    exit 1
}

# Check if credential file exists in dashboard directory
$defaultCredPath = Join-Path $dashboardDir "firebase-service-account.json"
if (-not $FirebaseCredentialPath -and -not (Test-Path $defaultCredPath)) {
    Write-Host ""
    Write-Warning "firebase-service-account.json not found in $dashboardDir"
    Write-Host "Download it from Firebase Console > Project Settings > Service Accounts > Generate New Private Key" -ForegroundColor Yellow
    Write-Host "Then place it at: $defaultCredPath" -ForegroundColor Yellow
    Write-Host ""
    exit 1
}

Write-Host "=== Starting LogSystem Dashboard (Firebase) ===" -ForegroundColor Cyan
Write-Host "URL: https://localhost:$Port" -ForegroundColor Green
Write-Host "Swagger: https://localhost:$Port/swagger" -ForegroundColor Green
Write-Host "Press Ctrl+C to stop." -ForegroundColor Yellow
Write-Host ""

$env:ASPNETCORE_URLS = "https://localhost:$Port"
$env:Dashboard__ApiKey = $ApiKey

if ($FirebaseProjectId) {
    $env:Firebase__ProjectId = $FirebaseProjectId
}
if ($FirebaseCredentialPath) {
    $env:Firebase__CredentialPath = $FirebaseCredentialPath
}

dotnet run --project $projectPath

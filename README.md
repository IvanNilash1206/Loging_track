# LogSystem — Endpoint Monitoring Agent & Dashboard

A lightweight Windows endpoint monitoring system for internal teams (20-25 machines).  
Built with **C# / .NET 8**, runs as a Windows Service, no kernel drivers required.

---

## What It Detects

| Capability | Confidence |
|---|---|
| File operations (USB, local, network shares) | 100% — exact file names |
| Cloud sync folder activity (OneDrive, Google Drive) | 100% — exact file names |
| Browser/app file uploads | High probability (correlation-based) |
| Transfers ≥ 25 MB | 100% |
| Slow/continuous exfiltration | High probability |
| Application usage & window titles | 100% |
| Encrypted traffic content | Not attempted (by design) |

---

## Architecture

```
┌──────────────────────────────────────────────────┐
│  Each Windows Endpoint                           │
│                                                  │
│  LogSystem.Agent (Windows Service)               │
│  ├── Module 1: File Monitor (FileSystemWatcher)  │
│  ├── Module 2: App Monitor (Win32 API polling)   │
│  ├── Module 3: Network Monitor (TCP table)       │
│  ├── Module 4: Correlation Engine (rules)        │
│  ├── Local Encrypted Queue (AES-256-GCM)         │
│  └── Secure Log Uploader (HTTPS + API key)       │
│                                                  │
└───────────────── HTTPS/TLS ──────────────────────┘
                      │
┌─────────────────────▼────────────────────────────┐
│  LogSystem.Dashboard (ASP.NET Core Web API)      │
│  ├── REST API for log ingestion                  │
│  ├── Firebase Firestore (Cloud NoSQL)            │
│  ├── Query endpoints (alerts, files, network)    │
│  └── Web dashboard (static HTML + JS)            │
└──────────────────────────────────────────────────┘
```

---

## Project Structure

```
LogSystem/
├── LogSystem.sln
├── scripts/
│   ├── Install-Agent.ps1       # Deploy agent as Windows Service
│   ├── Uninstall-Agent.ps1     # Remove agent service
│   └── Start-Dashboard.ps1     # Run dashboard locally
├── src/
│   ├── LogSystem.Shared/       # Shared models & configuration
│   │   ├── Models/
│   │   │   ├── FileEvent.cs
│   │   │   ├── NetworkEvent.cs
│   │   │   ├── AppUsageEvent.cs
│   │   │   ├── AlertEvent.cs
│   │   │   ├── DeviceInfo.cs
│   │   │   └── LogBatch.cs
│   │   └── Configuration/
│   │       └── AgentConfiguration.cs
│   ├── LogSystem.Agent/        # Endpoint agent (Windows Service)
│   │   ├── Monitors/
│   │   │   ├── FileMonitorService.cs
│   │   │   ├── AppMonitorService.cs
│   │   │   ├── NetworkMonitorService.cs
│   │   │   └── CorrelationEngine.cs
│   │   ├── Services/
│   │   │   ├── LocalEventQueue.cs
│   │   │   └── LogUploaderService.cs
│   │   ├── NativeMethods.cs
│   │   ├── Worker.cs
│   │   ├── Program.cs
│   │   └── appsettings.json
│   └── LogSystem.Dashboard/    # Backend API + Web UI
│       ├── Controllers/
│       │   ├── LogIngestionController.cs
│       │   └── DashboardController.cs
│       ├── Data/
│       │   ├── FirestoreService.cs
│       │   └── LogSystemDbContext.cs
│       ├── wwwroot/
│       │   └── index.html
│       ├── Program.cs
│       └── appsettings.json
```

---

## Prerequisites

- Windows 10/11 or Windows Server 2019+
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Firebase project](https://console.firebase.google.com) with Firestore enabled
- Firebase Service Account key (JSON file)
- Administrator privileges (for Windows Service installation)

> **First time?** See the full [Setup Guide](docs/SETUP_GUIDE.md) for step-by-step Firebase & API key instructions.

---

## Quick Start

### 1. Build the solution

```powershell
dotnet build LogSystem.sln
```

### 2. Start the Dashboard

```powershell
.\scripts\Start-Dashboard.ps1
# Or manually:
cd src/LogSystem.Dashboard
dotnet run
```

Open `https://localhost:5001` for the web dashboard.  
Open `https://localhost:5001/swagger` for the API docs.

### 3. Run the Agent (development mode)

```powershell
cd src/LogSystem.Agent
dotnet run
```

### 4. Install Agent as Windows Service (production)

```powershell
# Run as Administrator
.\scripts\Install-Agent.ps1 -ApiEndpoint "https://your-server:5001" -ApiKey "your-secure-key"
```

### 5. Uninstall Agent

```powershell
# Run as Administrator
.\scripts\Uninstall-Agent.ps1
```

---

## Configuration

Edit `src/LogSystem.Agent/appsettings.json`:

| Setting | Default | Description |
|---|---|---|
| `ApiEndpoint` | `https://localhost:5001` | Dashboard server URL |
| `ApiKey` | `CHANGE_ME...` | Shared secret for authentication |
| `UploadIntervalSeconds` | `60` | How often to upload batches |
| `FileMonitor.Enabled` | `true` | Track file operations |
| `FileMonitor.MonitorUsb` | `true` | Watch removable drives |
| `AppMonitor.PollingIntervalMs` | `3000` | Window title poll rate |
| `NetworkMonitor.PollingIntervalMs` | `5000` | TCP table poll rate |
| `Correlation.LargeTransferThresholdBytes` | `26214400` | 25 MB alert threshold |
| `Security.EncryptLocalQueue` | `true` | AES-256-GCM local encryption |

---

## Correlation Rules

### Rule 1 — Large Transfer
```
IF process outbound_bytes >= 25 MB → ALERT: LargeTransfer
```

### Rule 2 — Continuous Small Transfers
```
IF total outbound > 30 MB in 10-minute window
AND multiple connections → ALERT: ContinuousTransfer
```

### Rule 3 — Probable File Upload
```
IF file read event
AND same process sends > 5 MB within 15 seconds → ALERT: ProbableUpload
```

---

## API Endpoints

| Method | Path | Description |
|---|---|---|
| `POST` | `/api/logs/ingest` | Receive log batch from agent |
| `GET` | `/api/dashboard/summary` | Summary statistics |
| `GET` | `/api/dashboard/alerts` | Alert events |
| `GET` | `/api/dashboard/file-events` | File activity |
| `GET` | `/api/dashboard/network-events` | Network activity |
| `GET` | `/api/dashboard/app-usage` | Application usage |
| `GET` | `/api/dashboard/devices` | Registered devices |
| `GET` | `/api/dashboard/top-talkers` | Highest network users |

Query parameters: `?deviceId=...&hours=24&limit=100&severity=High`

---

## Security

- **TLS 1.2+** enforced on all agent-to-server communication
- **API key authentication** on every upload
- **AES-256-GCM** encryption for local queue files
- **Key derivation** via PBKDF2 (100,000 iterations, SHA-256)
- **Tamper detection** on queue files
- **RBAC-ready** — extend the dashboard with role-based access

---

## Legal Requirements (Do Not Skip)

Before deploying to any endpoint:

1. ✅ Written monitoring policy approved by management
2. ✅ Employee acknowledgment form signed
3. ✅ Deploy only on company-owned devices
4. ✅ Define log retention period (default: 90 days)
5. ✅ Restrict dashboard access to authorized administrators

---

## Development Roadmap

- **Phase 1** (Week 1-2): File logging, app tracking, cloud upload
- **Phase 2** (Week 3-4): USB detection, network tracking, large transfer alerts
- **Phase 3** (Week 5-6): Correlation engine, continuous transfer detection, dashboard analytics

---

## Full Setup Guide

See **[docs/SETUP_GUIDE.md](docs/SETUP_GUIDE.md)** for detailed instructions on:
- Creating a Firebase project & Firestore database
- Downloading the service account key
- Generating and configuring API keys
- Deploying Firestore indexes and security rules
- Troubleshooting common issues

---

## License

Internal use only. Not for distribution.
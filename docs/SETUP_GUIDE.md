# LogSystem — Complete API Key & Firebase Setup Guide

This guide walks you through **every key, credential, and configuration** needed to run the LogSystem from scratch.

---

## Table of Contents

1. [Overview of All Keys](#1-overview-of-all-keys)
2. [Firebase Project Setup](#2-firebase-project-setup)
3. [Get Firebase Service Account Key](#3-get-firebase-service-account-key)
4. [Create Firestore Database](#4-create-firestore-database)
5. [Create Firestore Indexes](#5-create-firestore-indexes)
6. [Deploy Firestore Security Rules](#6-deploy-firestore-security-rules)
7. [Generate the Dashboard API Key](#7-generate-the-dashboard-api-key)
8. [Configure the Dashboard](#8-configure-the-dashboard)
9. [Configure the Agent](#9-configure-the-agent)
10. [Verify Everything Works](#10-verify-everything-works)
11. [Optional: Firebase Hosting for Dashboard](#11-optional-firebase-hosting-for-dashboard)
12. [Security Checklist](#12-security-checklist)

---

## 1. Overview of All Keys

| Key / Credential | Where It's Used | How to Get It |
|---|---|---|
| **Firebase Service Account JSON** | Dashboard server | Firebase Console → Service Accounts |
| **Firebase Project ID** | Dashboard `appsettings.json` | Firebase Console → Project Settings |
| **Dashboard API Key** (custom) | Agent ↔ Dashboard auth | You generate it (any strong secret) |
| **Agent DeviceId** | Each agent instance | Auto-generated or manually set |

> **Important:** There is NO Firebase "Web API Key" used in this project. We use the **Admin SDK** with a Service Account, which is more secure and appropriate for server-to-server communication.

---

## 2. Firebase Project Setup

### Step 1: Create a Firebase Project

1. Go to [https://console.firebase.google.com](https://console.firebase.google.com)
2. Click **"Create a project"** (or "Add project")
3. Enter a project name: `LogSystem` (or your preferred name)
4. **Google Analytics**: You can disable this — it's not needed for LogSystem
5. Click **"Create project"**
6. Wait for provisioning (~30 seconds)
7. Click **"Continue"**

### Step 2: Note Your Project ID

1. In the Firebase Console, click the **gear icon** (⚙️) next to "Project Overview"
2. Click **"Project settings"**
3. Under the **General** tab, find **"Project ID"**
4. Copy it — it looks like: `logsystem-abc123`

> This is your `Firebase:ProjectId` value.

---

## 3. Get Firebase Service Account Key

This is the **most important credential** — it authenticates your Dashboard server to Firestore.

### Step-by-Step:

1. In Firebase Console → **Project Settings** (gear icon)
2. Click the **"Service accounts"** tab
3. You'll see "Firebase Admin SDK" section
4. Make sure **"C#"** or **.NET** is shown (or just "Node.js" — the key is the same)
5. Click **"Generate new private key"**
6. Click **"Generate key"** in the confirmation dialog
7. A JSON file downloads — it looks like:

```json
{
  "type": "service_account",
  "project_id": "logsystem-abc123",
  "private_key_id": "abc123...",
  "private_key": "-----BEGIN PRIVATE KEY-----\n...\n-----END PRIVATE KEY-----\n",
  "client_email": "firebase-adminsdk-xxxxx@logsystem-abc123.iam.gserviceaccount.com",
  "client_id": "123456789",
  "auth_uri": "https://accounts.google.com/o/oauth2/auth",
  "token_uri": "https://oauth2.googleapis.com/token",
  "auth_provider_x509_cert_url": "https://www.googleapis.com/oauth2/v1/certs",
  "client_x509_cert_url": "https://www.googleapis.com/robot/v1/metadata/x509/...",
  "universe_domain": "googleapis.com"
}
```

### Step 3: Place the Key File

1. Rename the downloaded file to: `firebase-service-account.json`
2. Place it in the Dashboard project directory:

```
src/LogSystem.Dashboard/firebase-service-account.json
```

> **NEVER commit this file to git.** It's already in `.gitignore`.

---

## 4. Create Firestore Database

### Step-by-Step:

1. In Firebase Console, click **"Build"** in the left sidebar
2. Click **"Firestore Database"**
3. Click **"Create database"**
4. Choose a location:
   - **For Middle East/UAE**: Pick `me-central1` (Doha) or `europe-west1` (Belgium)
   - **For US**: Pick `us-central1` (Iowa) or `us-east1` (South Carolina)
   - **For Europe**: Pick `europe-west1` (Belgium)
5. Start in **"Production mode"** (our security rules deny all client access anyway)
6. Click **"Create"**

> The database is now created. It starts empty — the Dashboard will create collections automatically when agents start sending data.

### Firestore Collections (Created Automatically)

| Collection | Description |
|---|---|
| `devices` | Registered agent endpoints |
| `file_events` | File activity logs |
| `network_events` | Network traffic logs |
| `app_usage_events` | Application usage logs |
| `alert_events` | Correlation engine alerts |

---

## 5. Create Firestore Indexes

The Dashboard uses **composite queries** that require Firestore indexes.

### Option A: Use Firebase CLI (Recommended)

```powershell
# Install Firebase CLI (requires Node.js)
npm install -g firebase-tools

# Login to Firebase
firebase login

# Navigate to project root
cd C:\Users\strka\Desktop\Log_System

# Initialize Firebase (select Firestore only)
firebase init firestore
# When asked for the project, select your LogSystem project
# When asked for rules file, point to: firebase/firestore.rules
# When asked for indexes file, point to: firebase/firestore.indexes.json

# Deploy indexes
firebase deploy --only firestore:indexes
```

### Option B: Let Firestore Auto-Create

If you skip this step, Firestore will show error messages with direct links to create the missing indexes. Click each link and Firestore will create them for you. This works but is slower.

---

## 6. Deploy Firestore Security Rules

### Option A: Firebase CLI

```powershell
firebase deploy --only firestore:rules
```

### Option B: Firebase Console

1. Go to **Firestore Database** → **Rules** tab
2. Replace the default rules with the content of `firebase/firestore.rules`
3. Click **"Publish"**

---

## 7. Generate the Dashboard API Key

This is a **custom shared secret** used between the Agent and Dashboard. It is NOT a Firebase key.

### Generate a secure key:

**Option A — PowerShell:**
```powershell
[Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Maximum 256 }) -as [byte[]])
```

**Option B — .NET:**
```powershell
dotnet script -e "Console.WriteLine(Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));"
```

**Option C — Use any password generator** to create a 32+ character random string.

Example output:
```
k8Fj2mN9xQ4wL7hR3pT6vB1yD0sA5gE=
```

> Use this **same key** in both the Dashboard config and every Agent config.

---

## 8. Configure the Dashboard

Edit `src/LogSystem.Dashboard/appsettings.json`:

```json
{
  "Firebase": {
    "ProjectId": "logsystem-abc123",           ← YOUR actual project ID
    "CredentialPath": "firebase-service-account.json"
  },
  "Dashboard": {
    "ApiKey": "k8Fj2mN9xQ4wL7hR3pT6vB1yD0sA5gE="  ← YOUR generated key
  }
}
```

### Verify:
- [ ] `firebase-service-account.json` exists in `src/LogSystem.Dashboard/`
- [ ] `ProjectId` matches your Firebase project
- [ ] `ApiKey` is a strong random string (32+ chars)

---

## 9. Configure the Agent

Edit `src/LogSystem.Agent/appsettings.json`:

```json
{
  "AgentConfiguration": {
    "DeviceId": "",
    "ApiEndpoint": "https://YOUR-DASHBOARD-SERVER:5001",  ← Dashboard URL
    "ApiKey": "k8Fj2mN9xQ4wL7hR3pT6vB1yD0sA5gE=",       ← SAME key as Dashboard
    ...
  }
}
```

| Field | Value |
|---|---|
| `DeviceId` | Leave empty = auto-generated as `HOSTNAME-USERNAME` |
| `ApiEndpoint` | URL where the Dashboard is running |
| `ApiKey` | **Must match** `Dashboard:ApiKey` exactly |

### For each endpoint machine:

The `ApiEndpoint` should point to wherever you deploy the Dashboard. Examples:

| Deployment | ApiEndpoint Value |
|---|---|
| Local dev | `https://localhost:5001` |
| LAN server | `https://192.168.1.100:5001` |
| Cloud VM | `https://logsystem.yourcompany.com` |

---

## 10. Verify Everything Works

### Step 1: Start the Dashboard

```powershell
cd C:\Users\strka\Desktop\Log_System
.\scripts\Start-Dashboard.ps1
```

You should see:
```
Connected to Firestore project: logsystem-abc123
```

### Step 2: Test the API

Open a new terminal:

```powershell
# Health check — should return empty data
Invoke-RestMethod -Uri "https://localhost:5001/api/dashboard/summary" -SkipCertificateCheck

# Test ingestion with a dummy batch
$headers = @{
    "X-Api-Key" = "YOUR_API_KEY_HERE"
    "Content-Type" = "application/json"
}

$body = @{
    deviceId = "TEST-DEVICE"
    sentAt = (Get-Date).ToUniversalTime().ToString("o")
    fileEvents = @()
    networkEvents = @()
    appUsageEvents = @()
    alerts = @()
    deviceInfo = @{
        deviceId = "TEST-DEVICE"
        hostname = $env:COMPUTERNAME
        user = $env:USERNAME
        lastSeen = (Get-Date).ToUniversalTime().ToString("o")
        osVersion = [System.Environment]::OSVersion.ToString()
        agentVersion = "1.0.0"
    }
} | ConvertTo-Json -Depth 5

Invoke-RestMethod -Uri "https://localhost:5001/api/logs/ingest" -Method POST -Headers $headers -Body $body -SkipCertificateCheck
```

Expected response: `{ "received": 0 }`

### Step 3: Check Firestore

1. Go to Firebase Console → Firestore Database
2. You should see a `devices` collection with your test device document

### Step 4: Run the Agent

```powershell
cd src\LogSystem.Agent
dotnet run
```

The agent will start monitoring and uploading events to the Dashboard → Firestore.

---

## 11. Optional: Firebase Hosting for Dashboard

If you want to host the Dashboard web UI on Firebase Hosting:

```powershell
# Build the dashboard
dotnet publish src/LogSystem.Dashboard -c Release -o publish

# In the firebase directory
firebase init hosting
# Set public directory to: publish/wwwroot
# Configure as single-page app: No

firebase deploy --only hosting
```

Your dashboard will be accessible at `https://your-project.web.app`.

> Note: The API still needs to run on a server — Firebase Hosting only serves static files.

---

## 12. Security Checklist

Before going to production, verify:

| Item | Status |
|---|---|
| `firebase-service-account.json` is NOT in git | ☐ |
| Git repo is private | ☐ |
| Dashboard API Key is 32+ random characters | ☐ |
| Same API Key in both Dashboard and Agent configs | ☐ |
| Firestore security rules deny all client access | ☐ |
| Dashboard uses HTTPS (TLS 1.2+) | ☐ |
| Firebase project has only authorized team members | ☐ |
| Agent local queue encryption is enabled | ☐ |
| Employee monitoring policy is signed | ☐ |

---

## Quick Reference: Where Each Key Goes

```
┌──────────────────────────────────────────────────────────────────┐
│                                                                  │
│   Firebase Console                                               │
│   ├── Project ID ──────→ Dashboard appsettings.json              │
│   │                      Firebase:ProjectId                      │
│   │                                                              │
│   └── Service Account                                            │
│       Key (JSON) ──────→ Dashboard directory                     │
│                          firebase-service-account.json            │
│                                                                  │
│   Your Generated                                                 │
│   API Key ─────────────→ Dashboard appsettings.json              │
│            │               Dashboard:ApiKey                      │
│            │                                                     │
│            └───────────→ Agent appsettings.json (each endpoint)  │
│                           AgentConfiguration:ApiKey              │
│                                                                  │
│   Dashboard URL ───────→ Agent appsettings.json (each endpoint)  │
│                           AgentConfiguration:ApiEndpoint         │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
```

---

## Troubleshooting

| Error | Fix |
|---|---|
| `Grpc.Core.RpcException: Unauthenticated` | Service account JSON is missing or wrong path |
| `Firebase:ProjectId is required` | Add `ProjectId` to appsettings.json |
| `Unauthorized: Invalid API key` | Dashboard:ApiKey ≠ Agent:ApiKey — they must match |
| `The default credentials could not be found` | Set `Firebase:CredentialPath` or `GOOGLE_APPLICATION_CREDENTIALS` env var |
| Firestore returns "Missing index" errors | Deploy indexes: `firebase deploy --only firestore:indexes` or click the link in the error message |
| Agent shows "Upload failed: Connection refused" | Dashboard isn't running, or `ApiEndpoint` URL is wrong |

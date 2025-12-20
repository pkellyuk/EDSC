# EDSC - Quick Start Guide

## What Has Been Implemented

### Web Control UI (QR Code)
The desktop app serves a browser UI and shows a QR code so you can control your ship without installing a mobile app.

## Project Structure

```
EDSC/
?? EDSC.sln
?? config.json
?? src/
?  ?? EDSC/           # Shared models and view models
?  ?? EDSC.Desktop/   # Desktop app + HTTP server
```

## How It Works

1. Desktop app starts an HTTP server on port 9000.
2. The UI shows a QR code that points to `http://<pc-ip>:9000/web`.
3. Phone browser loads the web UI and sends HTTP commands.
4. The desktop app simulates keypresses for Elite Dangerous.

## Quick Start

```bash
cd src/EDSC.Desktop
dotnet build
dotnet run
```

Copy config:

```powershell
Copy-Item config.json src/EDSC.Desktop/bin/Debug/net8.0-windows/config.json -Force
```

## Testing

```bash
curl http://localhost:9000/
```

```bash
curl -X POST http://localhost:9000/command \
  -H "Content-Type: application/json" \
  -d '{"buttonId":"hardpoints","key":"U","timestamp":1234567890}'
```

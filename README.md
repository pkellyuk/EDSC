# EDSC - Elite Dangerous Ship Controls

Remote control your Elite Dangerous ship functions from your phone's browser.

[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-blue)](https://dotnet.microsoft.com/download)
[![AvaloniaUI](https://img.shields.io/badge/AvaloniaUI-11.0-purple)](https://avaloniaui.net/)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

## What is EDSC?

EDSC is a Windows desktop app that serves a web control panel for Elite Dangerous. Scan a QR code from the desktop UI and control ship functions from any phone browser on the same network.

**How it works:**
- Desktop app runs a local HTTP server
- UI shows a QR code to open `http://<pc-ip>:9000/web`
- Phone browser sends commands via HTTP
- Desktop app simulates keypresses in Elite Dangerous

## Features

- QR code web UI (no mobile app required)
- HTTP (port 9000) and HTTPS (port 9001) command server
- Voice control with continuous listening
- Keyboard simulation for Elite Dangerous
- JSON configuration for buttons (categories + SVG icons)
- IP selector with common addresses (192.168.x.x) prioritized

## Quick Start

### 1) Build and run the desktop app

```bash
cd src/EDSC.Desktop
dotnet build
dotnet run
```

You should see:
```
info: Now listening on: http://[::]:9000
info: Application started
```

### 2) Configuration

The button layout is stored in `config.json` (tracked in this repo). The desktop app reads it from:

`src/EDSC.Desktop/bin/Debug/net8.0-windows/config.json`

Buttons support categories and SVG icons for the web UI. Example:

```json
{
  "id": "hardpoints",
  "key": "U",
  "label": "Hardpoints",
  "category": "Combat",
  "iconSvg": "hardpoints.svg",
  "color": "#6B7280",
  "size": 80
}
```

SVG icons are served from `src/EDSC.Desktop/Assets/Icons` and are copied to the output on build.

Copy it there if needed:

```powershell
Copy-Item config.json src/EDSC.Desktop/bin/Debug/net8.0-windows/config.json -Force
```

### 3) Open the web UI

1. Select the correct IP address in the desktop app
2. Scan the QR code with your phone
3. Use the browser UI to trigger ship actions

## Project Structure

```
EDSC/
?? EDSC.sln
?? config.json                  # Current button layout
?? src/
?  ?? EDSC/                      # Shared models and view models
?  ?? EDSC.Desktop/              # Desktop app + HTTP server
?? installer/
?  ?? EDSC.iss                   # Inno Setup installer script
```

## API Reference

**Health check**
```
GET http://localhost:9000/
```

**Send command**
```
POST http://localhost:9000/command
Content-Type: application/json

{
  "buttonId": "hardpoints",
  "key": "U",
  "timestamp": 1234567890
}
```

## Web UI

Open in a phone browser:

```
http://<pc-ip>:9000/web
```

The web UI groups buttons by category and shows SVG icons above the labels. It also includes a Fullscreen toggle in the toolbar.

## Voice Commands

The web UI supports voice control for hands-free operation. Click the "Voice" button to enable continuous listening.

**How it works:**
- The browser listens continuously for speech
- When you say a button label (e.g., "left panel", "landing gear", "hardpoints"), it triggers that command
- Commands are matched using fuzzy matching, so slight variations are tolerated
- A 2-second cooldown prevents accidental double-firing

**Example voice commands:**
- "Left panel" - opens the left ship panel
- "Right panel" - opens the right ship panel
- "Comms panel" - opens the comms panel
- "Landing gear" - toggles landing gear
- "Cargo scoop" - toggles cargo scoop
- "Hardpoints" - deploys/retracts hardpoints

Voice commands use your browser's built-in speech recognition (Web Speech API). This requires an internet connection as most browsers use cloud-based speech recognition.

## HTTPS and Certificate Warning

The web UI is available on both HTTP (port 9000) and HTTPS (port 9001). HTTPS is required for voice commands on most mobile browsers, as the Web Speech API requires a secure context.

**Why you'll see a certificate warning:**

The app uses a self-signed SSL certificate generated at runtime. Your browser will show a security warning because the certificate is not signed by a trusted certificate authority. This is expected and safe for local network use.

**To proceed past the warning:**
- **Chrome/Edge**: Click "Advanced" then "Proceed to [IP address] (unsafe)"
- **Safari**: Click "Show Details" then "visit this website"
- **Firefox**: Click "Advanced" then "Accept the Risk and Continue"

The HTTPS connection encrypts traffic between your phone and PC, even though the certificate is self-signed.

## Troubleshooting

**Web UI does not load**
- Ensure phone and PC are on the same network
- Pick the correct IP in the desktop app
- Allow TCP port 9000 in Windows Firewall
- Test `http://PC_IP:9000/web` in a desktop browser

**Buttons not triggering reliably**
- Some games need longer key presses; the desktop app uses a short long-press (down → delay → up) by default.

**Voice commands not working**
- Use HTTPS (port 9001) - voice requires a secure context
- Accept the certificate warning in your browser
- Ensure microphone permission is granted
- Check that your browser supports Web Speech API (Chrome, Edge, Safari)

## Installer (EXE)

This repo includes an Inno Setup script for a classic unsigned EXE installer (no Appx/MSIX signing required).

1) Publish the desktop app:
```powershell
dotnet publish src/EDSC.Desktop/EDSC.Desktop.csproj -c Release -r win-x64 --self-contained true
```

2) Build the installer with Inno Setup:
```powershell
iscc installer/EDSC.iss
```

The output EXE is written to `installer/EDSC-Setup.exe`.

## Security Considerations

- No authentication; local network only
- HTTP is plain text

## License

MIT License - See LICENSE file for details

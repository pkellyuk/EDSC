# EDSC - Deployment Guide

Complete guide for deploying EDSC to production environments.

## Table of Contents

- [Overview](#overview)
- [PC Server Deployment](#pc-server-deployment)
- [Android App Deployment](#android-app-deployment)
- [Network Configuration](#network-configuration)
- [Elite Dangerous Integration](#elite-dangerous-integration)
- [Post-Deployment Testing](#post-deployment-testing)
- [Troubleshooting](#troubleshooting)

## Overview

EDSC deployment consists of two components:
1. **PC Server** - Runs on Windows PC where Elite Dangerous is installed
2. **Android App** - Installed on mobile device on same network

### Deployment Checklist

**PC Server:**
- [ ] Build standalone executable
- [ ] Create configuration file
- [ ] Configure Windows Firewall
- [ ] Test HTTP server
- [ ] Test UDP discovery
- [ ] (Optional) Install as Windows service

**Android App:**
- [ ] Build release APK
- [ ] Sign APK
- [ ] Install on device
- [ ] Grant permissions
- [ ] Test discovery
- [ ] Test button commands

**Network:**
- [ ] Both devices on same WiFi network
- [ ] Firewall allows UDP port 5001
- [ ] Firewall allows TCP port 5000
- [ ] Router not blocking broadcast packets

**Elite Dangerous:**
- [ ] Configure keybindings
- [ ] Test keys manually in-game
- [ ] Verify keys match config.json

## PC Server Deployment

### Method 1: Standalone Executable (Recommended)

**1. Publish the application:**

```bash
cd src/EDSC.Desktop

# Windows x64 self-contained
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true

# Output: bin/Release/net8.0-windows/win-x64/publish/EDSC.Desktop.exe
```

**2. Copy published files to deployment location:**

```
C:\Program Files\EDSC\
├── EDSC.Desktop.exe
└── config.json
```

**3. Create config.json:**

```json
{
  "server": {
    "port": 5000,
    "discoveryPort": 5001,
    "autoStart": true,
    "enableDiscovery": true
  },
  "buttons": [
    {
      "id": "shieldboost",
      "key": "F1",
      "icon": "🛡️",
      "color": "#4CAF50",
      "label": "Shield Boost",
      "size": 80
    },
    {
      "id": "ecm",
      "key": "F2",
      "icon": "⚡",
      "color": "#2196F3",
      "label": "ECM",
      "size": 80
    },
    {
      "id": "chaff",
      "key": "F3",
      "icon": "💨",
      "color": "#FF9800",
      "label": "Chaff",
      "size": 80
    },
    {
      "id": "heatsink",
      "key": "F4",
      "icon": "❄️",
      "color": "#00BCD4",
      "label": "Heat Sink",
      "size": 80
    }
  ]
}
```

**4. Configure Windows Firewall:**

Run PowerShell as Administrator:

```powershell
# Allow UDP discovery
New-NetFirewallRule -DisplayName "EDSC Discovery" `
  -Direction Inbound -Protocol UDP -LocalPort 5001 -Action Allow

# Allow HTTP commands
New-NetFirewallRule -DisplayName "EDSC HTTP" `
  -Direction Inbound -Protocol TCP -LocalPort 5000 -Action Allow
```

**5. Test the server:**

```powershell
# Start server
cd "C:\Program Files\EDSC"
.\EDSC.Desktop.exe

# In another PowerShell window, test health check
curl http://localhost:5000/
# Should return: {"service":"EDSC","status":"running","version":"1.0.0"}
```

**6. (Optional) Create desktop shortcut:**

Right-click `EDSC.Desktop.exe` → Send to → Desktop (create shortcut)

### Method 2: Windows Service (Advanced)

Install EDSC as a Windows service using NSSM (Non-Sucking Service Manager).

**1. Download NSSM:**
- https://nssm.cc/download
- Extract to `C:\nssm`

**2. Install service:**

```powershell
# Run as Administrator
cd C:\nssm\win64

# Install service
.\nssm.exe install EDSC "C:\Program Files\EDSC\EDSC.Desktop.exe"

# Configure service
.\nssm.exe set EDSC AppDirectory "C:\Program Files\EDSC"
.\nssm.exe set EDSC DisplayName "EDSC - Elite Dangerous Ship Controls"
.\nssm.exe set EDSC Description "HTTP command server and UDP discovery for EDSC mobile app"
.\nssm.exe set EDSC Start SERVICE_AUTO_START

# Start service
.\nssm.exe start EDSC
```

**3. Verify service:**

```powershell
# Check service status
Get-Service EDSC

# Check if server is responding
curl http://localhost:5000/
```

**4. Manage service:**

```powershell
# Stop service
Stop-Service EDSC

# Start service
Start-Service EDSC

# Restart service
Restart-Service EDSC

# Remove service
nssm.exe remove EDSC confirm
```

### Method 3: Task Scheduler (Auto-start on Login)

**1. Open Task Scheduler:**
- Windows Key + R → `taskschd.msc`

**2. Create new task:**
- Action → Create Task
- Name: "EDSC Server"
- Description: "Elite Dangerous Ship Controls server"
- Run whether user is logged on or not
- Run with highest privileges

**3. Triggers:**
- New trigger → At log on → Specific user → OK

**4. Actions:**
- New action → Start a program
- Program: `C:\Program Files\EDSC\EDSC.Desktop.exe`
- Start in: `C:\Program Files\EDSC`

**5. Conditions:**
- Uncheck "Start the task only if the computer is on AC power"

**6. Settings:**
- Allow task to be run on demand
- If task fails, restart every 1 minute, attempt 3 times

## Android App Deployment

### Method 1: Sideloading (Development/Personal Use)

**1. Build release APK:**

```bash
cd src/EDSC.Android

# Build release
dotnet publish -c Release -f net8.0-android
```

**2. Generate signing keystore (first time only):**

```bash
# Generate keystore
keytool -genkey -v -keystore edsc.keystore -alias edsc -keyalg RSA -keysize 2048 -validity 10000

# Enter password and details when prompted
# Store keystore file securely - needed for all future updates!
```

**3. Sign APK:**

Option A: Manual signing:
```bash
# Sign
jarsigner -verbose -sigalg SHA256withRSA -digestalg SHA-256 -keystore edsc.keystore bin/Release/net8.0-android/com.edsc.app.apk edsc

# Align (optimize)
zipalign -v 4 bin/Release/net8.0-android/com.edsc.app.apk bin/Release/net8.0-android/com.edsc.app-release.apk
```

Option B: Configure in project file (EDSC.Android.csproj):
```xml
<PropertyGroup Condition="'$(Configuration)' == 'Release'">
  <AndroidKeyStore>true</AndroidKeyStore>
  <AndroidSigningKeyStore>C:\path\to\edsc.keystore</AndroidSigningKeyStore>
  <AndroidSigningKeyAlias>edsc</AndroidSigningKeyAlias>
  <AndroidSigningKeyPass>your-password</AndroidSigningKeyPass>
  <AndroidSigningStorePass>your-password</AndroidSigningStorePass>
</PropertyGroup>
```

**4. Install on device:**

Method A: USB installation:
```bash
# Connect device via USB, enable USB debugging
adb devices

# Install APK
adb install bin/Release/net8.0-android/com.edsc.app-release.apk
```

Method B: Transfer and install manually:
1. Copy APK to device (email, cloud storage, USB)
2. On device: Settings → Security → Install from Unknown Sources (enable)
3. Use file manager to open APK
4. Tap "Install"

**5. Grant permissions:**

When first launching the app, grant required permissions:
- Network access (automatic)
- WiFi state access (automatic)

### Method 2: Google Play Store (Public Distribution)

**1. Create Google Play Developer account:**
- https://play.google.com/console
- One-time $25 registration fee

**2. Prepare store listing:**

Required assets:
- App icon (512x512 PNG)
- Feature graphic (1024x500 PNG)
- Screenshots (minimum 2, phone and tablet)
- App description (4000 char max)
- Short description (80 char max)
- Privacy policy URL

**3. Build App Bundle (AAB) for Play Store:**

```bash
cd src/EDSC.Android

# Build AAB (preferred by Play Store)
dotnet publish -c Release -f net8.0-android -p:AndroidPackageFormat=aab

# Output: bin/Release/net8.0-android/com.edsc.app.aab
```

**4. Upload to Play Console:**

1. Create new app in Play Console
2. Fill out store listing
3. Upload AAB file
4. Set pricing (free or paid)
5. Set content rating
6. Set target audience
7. Submit for review

**5. Wait for review:**
- Initial review: 1-3 days
- Updates: usually within 24 hours

### Method 3: Internal Distribution (Beta Testing)

**1. Use Google Play Internal Testing:**
- Upload AAB to Play Console
- Create internal test track
- Add testers by email
- Share generated link

**2. Use Firebase App Distribution:**
- Upload APK to Firebase console
- Add testers
- Testers receive email with download link

## Network Configuration

### Router Setup

**1. Reserve static IP for PC (recommended):**

Option A: Router DHCP reservation:
1. Access router admin panel (usually 192.168.1.1)
2. Find DHCP settings
3. Add reservation for PC's MAC address
4. Assign fixed IP (e.g., 192.168.1.100)

Option B: Windows static IP:
1. Network settings → Change adapter options
2. Right-click WiFi/Ethernet → Properties
3. IPv4 → Properties → Use the following IP address
4. IP: 192.168.1.100, Subnet: 255.255.255.0, Gateway: 192.168.1.1

**2. Verify broadcast support:**

Some routers block UDP broadcast by default.

Test:
```bash
# From another PC on network, send broadcast
echo '{"type":"discover","requestId":"test","timestamp":1234567890}' | nc -u -b 255.255.255.255 5001

# If PC responds, broadcast works
# If no response, check router settings for "AP Isolation" or "Client Isolation" and disable it
```

**3. Disable AP Isolation:**

If discovery doesn't work:
1. Router settings → WiFi settings
2. Look for "AP Isolation", "Client Isolation", or "Guest Network"
3. Disable isolation
4. Restart router

### Firewall Configuration

**Windows Firewall (PC):**

Already covered in PC Server Deployment. Verify rules:

```powershell
# List EDSC firewall rules
Get-NetFirewallRule -DisplayName "EDSC*"

# Should show:
# EDSC Discovery (UDP 5001 Inbound Allow)
# EDSC HTTP (TCP 5000 Inbound Allow)
```

**Third-party firewalls:**

If using Norton, McAfee, ZoneAlarm, etc., manually allow:
- UDP port 5001 inbound
- TCP port 5000 inbound
- Or allow entire EDSC.Desktop.exe application

**Android Firewall:**

Most Android devices don't have firewall by default. If using third-party firewall app, allow EDSC network access.

### Network Troubleshooting

**Test connectivity:**

```bash
# From mobile device, ping PC
ping 192.168.1.100

# If ping fails, check:
# - Both on same network
# - WiFi connected (not mobile data)
# - PC firewall allows ICMP (ping)
```

**Test HTTP server:**

```bash
# From mobile browser
http://192.168.1.100:5000/

# Should show: {"service":"EDSC","status":"running","version":"1.0.0"}
```

**Test UDP discovery:**

Use network debugging app on Android to send UDP broadcast and verify response.

## Elite Dangerous Integration

### Configure Keybindings

**1. Launch Elite Dangerous**

**2. Options → Controls → Miscellaneous**

Bind ship functions to match your config.json:

| Function | Recommended Key | config.json |
|----------|----------------|-------------|
| Shield Cell Bank | F1 | "key": "F1" |
| ECM | F2 | "key": "F2" |
| Chaff Launcher | F3 | "key": "F3" |
| Heat Sink | F4 | "key": "F4" |
| Night Vision | F5 | "key": "F5" |
| Cargo Scoop | F6 | "key": "F6" |
| Landing Gear | F7 | "key": "F7" |
| Flight Assist | F8 | "key": "F8" |

**3. Test keybindings manually:**

In-game, press each key to verify it activates the correct function.

**4. Update config.json if needed:**

If you prefer different keys, edit config.json on PC:

```json
{
  "buttons": [
    {
      "id": "shieldboost",
      "key": "F1",  // ← Change this to match your keybinding
      "icon": "🛡️",
      "color": "#4CAF50",
      "label": "Shield Boost",
      "size": 80
    }
  ]
}
```

Restart EDSC server after editing config.

### Supported Keys

See API.md for complete list. Common Elite Dangerous bindings:

**Function Keys:**
```
F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12
```

**Letters:**
```
A-Z (use for weapon groups, targeting, etc.)
```

**Numbers:**
```
0-9 (for power distribution, pips)
```

**Number Pad:**
```
NUMPAD0-NUMPAD9 (avoid if using for camera controls)
```

**Special:**
```
ESCAPE, ENTER, SPACE, TAB, DELETE, INSERT
```

**Modifiers:**
```
SHIFT, CONTROL, ALT
```

**Note:** For key combinations (Ctrl+F1), send keys sequentially:
1. Send "CONTROL" (key down)
2. Send "F1"
3. Send "CONTROL" (key up)

Currently, EDSC sends single keypresses only.

## Post-Deployment Testing

### Test Checklist

**PC Server:**
- [ ] Server starts without errors
- [ ] Logs show "Now listening on: http://[::]:5000"
- [ ] Health check responds: `curl http://localhost:5000/`
- [ ] Discovery listener started (check logs)
- [ ] Firewall rules active

**Android App:**
- [ ] App launches without crash
- [ ] Shows connection view
- [ ] Discovery finds PC server
- [ ] Can select discovered server
- [ ] Can connect to server
- [ ] Shows button grid
- [ ] Buttons are responsive

**End-to-End:**
- [ ] Discovery works from mobile
- [ ] Connection establishes
- [ ] Buttons send commands
- [ ] PC receives commands (check logs)
- [ ] Keys are simulated (test with Notepad)
- [ ] Elite Dangerous receives keys

### Test Procedure

**1. Test PC server in isolation:**

```powershell
# Start server
cd "C:\Program Files\EDSC"
.\EDSC.Desktop.exe

# Expected output:
# [Discovery] Starting UDP listener on port 5001
# [Discovery] UDP listener started successfully
# info: Now listening on: http://[::]:5000
```

**2. Test HTTP endpoint:**

```powershell
# Health check
curl http://localhost:5000/

# Expected: {"service":"EDSC","status":"running","version":"1.0.0"}

# Send test command
curl -X POST http://localhost:5000/command -H "Content-Type: application/json" -d '{\"buttonId\":\"test\",\"key\":\"F1\",\"timestamp\":1234567890}'

# Expected: {"success":true,"message":"Key 'F1' pressed","timestamp":...}
```

**3. Test keyboard simulation:**

Open Notepad on PC, then send command from PowerShell:

```powershell
curl -X POST http://localhost:5000/command -H "Content-Type: application/json" -d '{\"buttonId\":\"test\",\"key\":\"A\",\"timestamp\":1234567890}'

# Expected: Letter "A" appears in Notepad
```

**4. Test discovery from mobile:**

On Android device:
1. Open EDSC app
2. Tap "🔍 Discover Servers"
3. Wait 3 seconds
4. Should see PC in list: "EDSC-COMPUTERNAME"

**5. Test mobile connection:**

1. Select discovered server
2. Tap "Connect"
3. Should show button grid
4. Check PC logs for connection message

**6. Test button commands:**

1. Open Notepad on PC (to see keypresses)
2. On mobile, tap "Shield Boost" button (bound to F1)
3. Verify F1 appears in Notepad
4. Check PC logs for command received

**7. Test in Elite Dangerous:**

1. Launch Elite Dangerous
2. Ensure keybindings configured
3. Start game (supercruise or docked)
4. Tap mobile buttons
5. Verify functions activate in-game

## Troubleshooting

### Discovery Not Working

**Symptom:** Mobile app shows "No servers found"

**Causes and solutions:**

1. **Different WiFi networks:**
   - Check both devices connected to same WiFi
   - Mobile may prefer 5GHz, PC on 2.4GHz - ensure same SSID

2. **Mobile using cellular data:**
   - Disable mobile data
   - Force WiFi usage in Android settings

3. **Router blocking broadcast:**
   - Disable AP Isolation in router settings
   - Try different WiFi network
   - Use manual IP entry as workaround

4. **PC firewall blocking UDP:**
   ```powershell
   # Verify firewall rule
   Get-NetFirewallRule -DisplayName "EDSC Discovery"

   # If missing, recreate:
   New-NetFirewallRule -DisplayName "EDSC Discovery" -Direction Inbound -Protocol UDP -LocalPort 5001 -Action Allow
   ```

5. **PC server not running:**
   ```powershell
   # Check if listening on port 5001
   netstat -an | findstr :5001

   # Should show: UDP    0.0.0.0:5001    *:*
   ```

**Workaround:** Use manual IP entry:
1. Find PC IP: `ipconfig` → IPv4 Address
2. On mobile: Expand "Manual Connection"
3. Enter IP (e.g., 192.168.1.100) and port (5000)

### Connection Fails

**Symptom:** Mobile can discover PC but connection fails

**Causes and solutions:**

1. **PC firewall blocking HTTP:**
   ```powershell
   # Verify firewall rule
   Get-NetFirewallRule -DisplayName "EDSC HTTP"

   # If missing:
   New-NetFirewallRule -DisplayName "EDSC HTTP" -Direction Inbound -Protocol TCP -LocalPort 5000 -Action Allow
   ```

2. **HTTP server not running:**
   ```powershell
   # Check if listening on port 5000
   netstat -an | findstr :5000

   # Should show: TCP    0.0.0.0:5000    0.0.0.0:0    LISTENING
   ```

3. **Port already in use:**
   ```powershell
   # Find process using port 5000
   netstat -ano | findstr :5000

   # Kill process or change port in config.json
   ```

4. **Antivirus blocking:**
   - Check antivirus logs
   - Add EDSC.Desktop.exe to exclusions
   - Temporarily disable antivirus to test

**Test manually:**
```bash
# From mobile browser
http://192.168.1.100:5000/

# Should show JSON response
```

### Keys Not Working in Elite Dangerous

**Symptom:** Buttons send successfully but no effect in-game

**Causes and solutions:**

1. **Elite Dangerous keybindings don't match:**
   - Open Elite Dangerous → Options → Controls
   - Verify function bound to correct key
   - Update config.json to match

2. **Elite Dangerous not focused:**
   - Click on Elite Dangerous window
   - Keys only work when game has focus
   - Use borderless windowed mode

3. **Game in menu/map mode:**
   - Keys work in flight mode only
   - Exit galaxy map, system map, station services
   - Test in supercruise or normal space

4. **Key conflicts:**
   - Another program capturing keys (voice attack, etc.)
   - Temporarily close other gaming software
   - Try different keys (F9-F12 less commonly used)

**Test:**
1. Minimize Elite Dangerous
2. Open Notepad
3. Tap mobile buttons
4. Verify keys appear in Notepad
5. If yes, problem is Elite Dangerous config, not EDSC

### Mobile App Crashes

**Symptom:** App crashes on startup or when tapping buttons

**Solutions:**

1. **Reinstall app:**
   ```bash
   adb uninstall com.edsc.app
   adb install bin/Release/net8.0-android/com.edsc.app-release.apk
   ```

2. **Clear app data:**
   - Android settings → Apps → EDSC → Storage → Clear data

3. **Check Android version:**
   - Requires Android 8.0 (API 26) or higher
   - Check device compatibility

4. **Check logs:**
   ```bash
   adb logcat | grep -i edsc
   # Look for crash stack traces
   ```

### Performance Issues

**Symptom:** Slow response, lag between button tap and activation

**Solutions:**

1. **Use 5GHz WiFi:**
   - Lower latency than 2.4GHz
   - Both devices should be on 5GHz

2. **Reduce WiFi interference:**
   - Move closer to router
   - Reduce obstacles between devices and router
   - Change WiFi channel if congested

3. **Use wired ethernet for PC:**
   - Eliminates WiFi latency on PC side

4. **Close background apps:**
   - On mobile: Close unnecessary apps
   - On PC: Close CPU-intensive applications

5. **Check network load:**
   - Pause downloads/uploads
   - Reduce streaming on network

## Security Considerations

### Current Implementation

EDSC has **NO authentication or encryption**:
- Anyone on network can discover server
- Anyone on network can send commands
- All traffic is plain text

### Recommendations

**For personal use:**
1. **Use trusted networks only** (home WiFi)
2. **Don't use on public WiFi** (coffee shops, hotels)
3. **Don't expose to internet** (no port forwarding)
4. **Use strong WiFi password**

**For shared networks:**
Consider implementing:
1. **Shared secret** - Discovery requires passphrase
2. **HTTPS** - Encrypt HTTP traffic
3. **API keys** - Require authentication header
4. **mDNS** - Instead of broadcast (more secure)

### Future Enhancements

Potential security additions:
- Passphrase in discovery request/response
- TLS/SSL for HTTP server (self-signed cert)
- Command rate limiting
- IP whitelist
- Session tokens

Currently, security relies on **network isolation** - only deploy on trusted networks.

## Backup and Recovery

### Backup Important Files

**PC Server:**
```
C:\Program Files\EDSC\
├── config.json          ← Backup this
└── edsc.keystore        ← If using for Android signing
```

**Android:**
- No persistent data stored
- Settings stored in app (backed up via Android backup)

### Configuration Backup

Create backup script:

```powershell
# backup-edsc.ps1
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupDir = "$env:USERPROFILE\EDSC-Backups\$timestamp"

New-Item -ItemType Directory -Path $backupDir -Force
Copy-Item "C:\Program Files\EDSC\config.json" $backupDir
Compress-Archive -Path $backupDir -DestinationPath "$backupDir.zip"

Write-Host "Backup created: $backupDir.zip"
```

Run before making config changes.

### Disaster Recovery

**PC server lost:**
1. Reinstall from published files
2. Restore config.json from backup
3. Reconfigure firewall rules

**Android app lost:**
1. Reinstall APK
2. Reconfigure server connection (discovery will find it)

**Config lost:**
1. Use config.example.json as template
2. Reconfigure Elite Dangerous keybindings

## Monitoring and Maintenance

### Check Server Status

**Via HTTP:**
```powershell
curl http://localhost:5000/
```

**Via PowerShell:**
```powershell
# Check if process running
Get-Process | Where-Object {$_.ProcessName -like "*EDSC*"}

# Check if ports listening
netstat -an | findstr :5000
netstat -an | findstr :5001
```

### Log Monitoring

EDSC uses Debug.WriteLine - view logs with:

**DebugView (SysInternals):**
1. Download: https://learn.microsoft.com/en-us/sysinternals/downloads/debugview
2. Run as Administrator
3. Capture → Capture Global Win32
4. See EDSC log messages

### Automatic Updates

**PC Server:**
1. Build new version
2. Stop old service
3. Replace executable
4. Start service
5. Test

**Android App:**
1. Build new APK/AAB
2. Sign with same keystore
3. Distribute via Play Store or sideload
4. Users upgrade via normal update mechanism

## Support

For deployment issues:
1. Check this guide first
2. Review troubleshooting section
3. Check firewall and network settings
4. Test with manual tools (curl, ping, etc.)
5. File issue on GitHub with deployment details

---

**Deployment Version**: 1.0.0
**Last Updated**: December 2025

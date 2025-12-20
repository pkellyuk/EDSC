# EDSC - Elite Dangerous Ship Controls

**Remote control your Elite Dangerous ship functions from your Android phone**

[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-blue)](https://dotnet.microsoft.com/download)
[![AvaloniaUI](https://img.shields.io/badge/AvaloniaUI-11.0-purple)](https://avaloniaui.net/)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

## 🎮 What is EDSC?

EDSC is a cross-platform application that lets you control Elite Dangerous ship functions (Shield Boost, ECM, Chaff, Heat Sink, etc.) from your Android mobile device. No more reaching for F-keys during combat!

**How it works:**
- PC runs a headless server that simulates keyboard input
- Android app discovers PC on network automatically (UDP broadcast)
- Mobile app sends commands via HTTP to PC
- PC simulates keypresses to Elite Dangerous
- Your ship activates the requested function!

## ✨ Features

### PC Server (Windows)
- ✅ **Headless Operation** - Runs in background, no window needed
- ✅ **HTTP Command Server** - RESTful API on port 5000
- ✅ **Automatic Discovery** - UDP broadcast listener on port 5001
- ✅ **Keyboard Simulation** - Sends keypresses to Elite Dangerous
- ✅ **JSON Configuration** - Customize buttons and settings
- ✅ **Comprehensive Logging** - Debug.WriteLine at every step

### Mobile App (Android)
- ✅ **Auto-Discovery** - Finds PC servers on network automatically
- ✅ **Manual Fallback** - Enter IP address if discovery fails
- ✅ **Touch-Optimized UI** - Large buttons, easy to tap in VR/combat
- ✅ **Customizable Buttons** - Icons, colors, labels from config
- ✅ **Real-time Feedback** - Shows command status and errors
- ✅ **Connection Testing** - Validates server before showing buttons

## 🚀 Quick Start

### Prerequisites

- **PC**: Windows with .NET 8.0 SDK
- **Mobile**: Android device with same WiFi network as PC
- **Game**: Elite Dangerous with configured keybindings

### 1. Build and Run PC Server

```bash
# Clone or navigate to project
cd EDSC

# Build the desktop app
cd src/EDSC.Desktop
dotnet build

# Run the server
dotnet run
```

You should see:
```
info: Now listening on: http://[::]:5000
info: Application started
```

### 2. Create Configuration

Create `config.json` in `src/EDSC.Desktop/bin/Debug/net8.0-windows/`:

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

See `config.example.json` for a complete example.

### 3. Configure Windows Firewall

The PC server needs two firewall rules:

**Option A: PowerShell (Run as Administrator)**
```powershell
# Allow UDP discovery
New-NetFirewallRule -DisplayName "EDSC Discovery" -Direction Inbound -Protocol UDP -LocalPort 5001 -Action Allow

# Allow HTTP commands
New-NetFirewallRule -DisplayName "EDSC HTTP" -Direction Inbound -Protocol TCP -LocalPort 5000 -Action Allow
```

**Option B: Manual**
- First run will trigger firewall prompts
- Click "Allow" for both UDP and TCP

### 4. Build Android App

```bash
# Open solution in Visual Studio or Rider
# Set EDSC.Android as startup project
# Deploy to Android device or emulator
```

Or use command line:
```bash
cd src/EDSC.Android
dotnet build
# Deploy using your preferred method
```

### 5. Connect and Control

1. **Launch PC server** (should auto-start discovery and HTTP server)
2. **Open EDSC app on Android**
3. **Tap "🔍 Discover Servers"** (should find your PC automatically)
4. **Select your PC from the list**
5. **Tap "Connect"**
6. **Start Elite Dangerous**
7. **Tap buttons to activate ship functions!**

## 📁 Project Structure

```
EDSC/
├── README.md                          # This file
├── EDSC.sln                          # Visual Studio solution
├── src/
│   ├── EDSC/                         # 📦 Shared Library (cross-platform)
│   │   ├── EDSC.csproj
│   │   ├── App.axaml                 # Base Avalonia app
│   │   ├── Models/
│   │   │   ├── Discovery/            # Network discovery protocol
│   │   │   ├── ServerConfig.cs       # Server configuration
│   │   │   ├── ButtonConfig.cs       # Button configuration
│   │   │   ├── AppConfig.cs          # Complete app config
│   │   │   └── CommandRequest.cs     # HTTP command protocol
│   │   ├── Services/
│   │   │   ├── Discovery/
│   │   │   │   ├── IDiscoveryService.cs          # Discovery interface
│   │   │   │   ├── UdpDiscoveryServicePC.cs      # PC discovery (listener)
│   │   │   │   └── UdpDiscoveryServiceAndroid.cs # Mobile discovery (broadcaster)
│   │   │   ├── IConfigurationService.cs          # Config interface
│   │   │   ├── JsonConfigurationService.cs       # JSON config loader
│   │   │   ├── ICommandServer.cs                 # HTTP server interface
│   │   │   ├── ICommandClient.cs                 # HTTP client interface
│   │   │   ├── HttpCommandClient.cs              # Mobile HTTP client
│   │   │   └── IKeyboardService.cs               # Keyboard interface
│   │   ├── ViewModels/
│   │   │   ├── ConnectionViewModel.cs            # Connection screen logic
│   │   │   └── MainViewModel.cs                  # Button grid logic
│   │   └── Views/
│   │       ├── ConnectionView.axaml              # Connection UI
│   │       └── MainView.axaml                    # Button grid UI
│   │
│   ├── EDSC.Desktop/                 # 🖥️ Windows Desktop App
│   │   ├── EDSC.Desktop.csproj
│   │   ├── Program.cs                            # Entry point
│   │   ├── DesktopApp.cs                         # Desktop app class
│   │   └── Services/
│   │       ├── HttpCommandServer.cs              # ASP.NET Core HTTP server
│   │       └── WindowsKeyboardService.cs         # Keyboard simulation
│   │
│   └── EDSC.Android/                 # 📱 Android Mobile App
│       ├── EDSC.Android.csproj
│       ├── MainActivity.cs                       # Android entry point
│       └── AndroidApp.cs                         # Android app class
│
├── config.example.json                # Example configuration
├── DISCOVERY_INTEGRATION.md           # Discovery feature guide
├── PROJECT_SUMMARY.md                 # Complete project summary
└── QUICK_START.md                     # Quick reference guide
```

## 🔧 Configuration Reference

### Server Configuration

```json
{
  "server": {
    "port": 5000,              // HTTP command server port
    "discoveryPort": 5001,     // UDP discovery port
    "autoStart": true,         // Start HTTP server automatically
    "enableDiscovery": true    // Enable UDP discovery service
  }
}
```

### Button Configuration

```json
{
  "id": "unique_id",           // Unique button identifier
  "key": "F1",                 // Keyboard key to press (F1-F12, A-Z, etc.)
  "icon": "🛡️",               // Icon to display (emoji or text)
  "color": "#4CAF50",          // Button background color (hex)
  "label": "Shield Boost",     // Button label text
  "size": 80                   // Button size in pixels
}
```

### Supported Key Names

- **Function Keys**: `F1`, `F2`, ... `F12`
- **Letters**: `A`, `B`, `C`, ... `Z`
- **Numbers**: `0`, `1`, `2`, ... `9`
- **Special**: `Escape`, `Enter`, `Space`, `Tab`, `Shift`, `Control`, `Alt`
- **Navigation**: `Up`, `Down`, `Left`, `Right`, `Home`, `End`, `PageUp`, `PageDown`

## 🌐 API Reference

### Health Check
```http
GET http://localhost:5000/
```

**Response:**
```json
{
  "service": "EDSC",
  "status": "running",
  "version": "1.0.0"
}
```

### Send Command
```http
POST http://localhost:5000/command
Content-Type: application/json

{
  "buttonId": "shieldboost",
  "key": "F1",
  "timestamp": 1234567890
}
```

**Success Response (200):**
```json
{
  "success": true,
  "message": "Key 'F1' pressed",
  "timestamp": 1234567890
}
```

**Error Response (400):**
```json
{
  "success": false,
  "message": "Key is required",
  "timestamp": 1234567890
}
```

## 🔍 Discovery Protocol

### Mobile → PC (UDP Broadcast to 255.255.255.255:5001)
```json
{
  "type": "discover",
  "requestId": "uuid-here",
  "timestamp": 1234567890
}
```

### PC → Mobile (UDP Response)
```json
{
  "type": "response",
  "requestId": "uuid-here",
  "serverName": "EDSC-COMPUTERNAME",
  "ipAddress": "192.168.1.100",
  "httpPort": 5000,
  "version": "1.0.0"
}
```

## 🛠️ Building from Source

### Requirements

- .NET 8.0 SDK or later
- Visual Studio 2022 / JetBrains Rider / VS Code (optional)
- Android SDK (for mobile app)

### Build Commands

**Restore dependencies:**
```bash
dotnet restore
```

**Build entire solution:**
```bash
dotnet build
```

**Build specific project:**
```bash
# PC Server
cd src/EDSC.Desktop
dotnet build

# Android App
cd src/EDSC.Android
dotnet build
```

**Run PC Server:**
```bash
cd src/EDSC.Desktop
dotnet run
```

**Publish standalone executable:**
```bash
cd src/EDSC.Desktop
dotnet publish -c Release -r win-x64 --self-contained
```

Output will be in `bin/Release/net8.0-windows/win-x64/publish/`

## 🧪 Testing

### Test PC Server

**1. Health check:**
```bash
curl http://localhost:5000/
```

**2. Test command (simulates F1):**
```bash
curl -X POST http://localhost:5000/command \
  -H "Content-Type: application/json" \
  -d '{"buttonId":"test","key":"F1","timestamp":1234567890}'
```

**3. Check logs:**
Look for `[Discovery]` and `[HttpCommandServer]` debug output

### Test Discovery

**From another machine on same network:**
```bash
# Send UDP broadcast (requires netcat or similar)
echo '{"type":"discover","requestId":"test-123","timestamp":1234567890}' | nc -u -b 255.255.255.255 5001
```

Should receive response from PC with its IP address.

## 🐛 Troubleshooting

### Discovery Not Working

**Problem**: Mobile can't find PC

**Solutions:**
1. Ensure both devices on same WiFi network
2. Check Windows Firewall allows UDP port 5001
3. Try manual IP entry as fallback
4. Restart both PC server and mobile app
5. Some routers block broadcast - use manual IP

### Connection Fails

**Problem**: Can discover but can't connect

**Solutions:**
1. Check Windows Firewall allows TCP port 5000
2. Verify PC server is actually running (check for "Now listening" message)
3. Test with browser: `http://PC_IP:5000/`
4. Check antivirus isn't blocking connections

### Keys Not Working in Game

**Problem**: Commands send successfully but nothing happens in game

**Solutions:**
1. Verify Elite Dangerous key bindings match config.json
2. Ensure Elite Dangerous window has focus
3. Test keys manually in-game first
4. Check game isn't in menu/map mode
5. Try different keys to rule out conflicts

### Multiple Network Adapters

**Problem**: PC responds with wrong IP address

**Solution:**
The `GetLocalIpAddress()` method returns first IPv4 address. If you have WiFi + Ethernet + VPN, it might pick the wrong one. Edit `UdpDiscoveryServicePC.cs` to prefer your WiFi subnet:

```csharp
// Prefer 192.168.x.x addresses (typical WiFi)
foreach (var ip in host.AddressList)
{
    if (ip.AddressFamily == AddressFamily.InterNetwork)
    {
        var ipStr = ip.ToString();
        if (ipStr.StartsWith("192.168."))
        {
            return ipStr;
        }
    }
}
```

### Build Errors

**Problem**: Build fails with errors

**Common Solutions:**
1. Ensure .NET 8.0 SDK installed: `dotnet --version`
2. Clean and rebuild: `dotnet clean && dotnet build`
3. Delete bin/ and obj/ folders, then rebuild
4. Restore NuGet packages: `dotnet restore`
5. Check project file targets correct framework

## 🔒 Security Considerations

### Current Implementation

- ❌ **No Authentication** - Anyone on network can send commands
- ❌ **No Encryption** - HTTP traffic is plain text
- ✅ **Local Network Only** - Doesn't work over internet
- ✅ **Discovery Unauthenticated** - By design for simplicity

### Recommendations

- **Use on trusted networks only** (home WiFi, not public WiFi)
- **Firewall rules** restrict to local subnet
- **Not for production** - This is a personal tool, not enterprise software

### Future Enhancements

- Add shared secret/passphrase for discovery
- Implement HTTPS with self-signed certificates
- Add command rate limiting
- Support mDNS instead of broadcast

## 📝 License

MIT License - See LICENSE file for details

## 🤝 Contributing

This is a personal project, but contributions are welcome!

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Submit a pull request

## 📧 Support

- **Issues**: File an issue on GitHub
- **Documentation**: See `PROJECT_SUMMARY.md` for architecture details
- **Discovery**: See `DISCOVERY_INTEGRATION.md` for discovery protocol details

## 🎯 Roadmap

### Completed ✅
- [x] UDP broadcast discovery
- [x] HTTP command server
- [x] Keyboard simulation
- [x] Mobile connection UI
- [x] Button grid UI
- [x] JSON configuration
- [x] Cross-platform architecture

### Planned 📋
- [ ] System tray integration (PC)
- [ ] Auto-start with Windows
- [ ] Multiple button presets
- [ ] Settings UI (mobile)
- [ ] Voice command support
- [ ] mDNS/Bonjour discovery
- [ ] HTTPS encryption
- [ ] Cloud configuration sync

## 💡 Tips & Tricks

### Elite Dangerous Setup

1. **Set keybindings** in Elite Dangerous to match your config.json
2. **Use simple keys** (F1-F12) to avoid conflicts
3. **Test keys manually** before using EDSC
4. **Bind commonly used functions** to buttons (Shield, Chaff, Heat Sink, etc.)

### Network Setup

1. **Use static IP** for PC (optional but helpful)
2. **Connect mobile to 5GHz WiFi** for lower latency
3. **Keep devices close** to WiFi access point
4. **Disable mobile data** to force WiFi usage

### Performance

1. **Run PC server headless** (no GUI overhead)
2. **Close unnecessary apps** on PC for lower latency
3. **Use wired ethernet** on PC if possible
4. **Keep mobile screen on** during gameplay

## 🙏 Credits

- **AvaloniaUI** - Cross-platform UI framework
- **InputSimulatorCore** - Keyboard simulation library
- **ASP.NET Core** - HTTP server framework
- **Elite Dangerous** - The amazing game this tool supports

## 📚 Additional Documentation

- **[PROJECT_SUMMARY.md](PROJECT_SUMMARY.md)** - Complete technical overview
- **[DISCOVERY_INTEGRATION.md](DISCOVERY_INTEGRATION.md)** - Discovery protocol details
- **[QUICK_START.md](QUICK_START.md)** - Quick reference guide
- **[EDSC.md](EDSC.md)** - Original design document

---

**Made with ❤️ for Elite Dangerous commanders o7**

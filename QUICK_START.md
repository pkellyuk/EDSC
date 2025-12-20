# EDSC - Quick Start Guide

## What Has Been Implemented

### ✅ Automatic Network Discovery (UDP Broadcast)
The mobile app can now automatically find the PC server on the same network subnet using UDP broadcast discovery.

## Project Structure

```
EDSC/
├── src/
│   ├── EDSC/                          # Shared code
│   │   ├── Models/
│   │   │   ├── Discovery/
│   │   │   │   ├── DiscoveryMessage.cs      # Discovery protocol models
│   │   │   │   └── DiscoveredServer.cs      # Server representation
│   │   │   └── ServerConfig.cs              # Server configuration
│   │   ├── Services/
│   │   │   └── Discovery/
│   │   │       ├── IDiscoveryService.cs             # Service interface
│   │   │       ├── UdpDiscoveryService.PC.cs        # PC implementation
│   │   │       └── UdpDiscoveryService.Android.cs   # Mobile implementation
│   │   ├── ViewModels/
│   │   │   └── ConnectionViewModel.cs       # Connection logic
│   │   └── Views/
│   │       ├── ConnectionView.axaml         # Mobile connection UI
│   │       └── ConnectionView.axaml.cs      # Code-behind
│   ├── EDSC.Desktop/                  # PC-specific code
│   │   └── App.axaml.cs.example            # PC integration example
│   └── EDSC.Android/                  # Android-specific code
│       └── MainActivity.cs.example         # Mobile integration example
├── EDSC.md                            # Original design document
├── DISCOVERY_INTEGRATION.md           # Detailed integration guide
├── QUICK_START.md                     # This file
└── config.example.json                # Example configuration

```

## How Discovery Works

1. **PC Server (Desktop)**
   - Listens on UDP port 5001
   - When it receives a discovery request, it responds with:
     - Server name (e.g., "EDSC-MyPC")
     - IP address (e.g., "192.168.1.100")
     - HTTP port (e.g., 5000)

2. **Mobile Client (Android)**
   - Broadcasts discovery request to entire subnet
   - Retries 3 times with 1-second timeout
   - Displays all discovered servers
   - Allows manual IP entry as fallback

## Key Features

### PC Side
- ✅ Automatic UDP listener startup
- ✅ Responds to discovery requests
- ✅ Configurable ports (HTTP + UDP)
- ✅ Can enable/disable discovery
- ✅ Comprehensive debug logging

### Mobile Side
- ✅ One-tap server discovery
- ✅ Displays discovered servers in list
- ✅ Manual IP entry fallback
- ✅ Connection status display
- ✅ Cancellable discovery process
- ✅ Comprehensive debug logging

## Next Steps to Complete EDSC

The discovery feature is complete. To finish the full application, you need to:

### Phase 1: HTTP Command Infrastructure
- [ ] Implement HTTP server on PC (using Kestrel)
- [ ] Implement HTTP client on mobile
- [ ] Define command protocol (JSON over HTTP)
- [ ] Add connection persistence (save last connected server)

### Phase 2: PC Functionality
- [ ] Implement keyboard simulation (Windows.Input.Simulator)
- [ ] Create button configuration loader
- [ ] Add system tray integration
- [ ] Implement auto-start functionality

### Phase 3: Mobile UI
- [ ] Create main button grid view
- [ ] Implement button tap handlers (send HTTP commands)
- [ ] Add settings view
- [ ] Show connection status indicator

### Phase 4: Testing & Polish
- [ ] Test on actual Elite Dangerous game
- [ ] Handle edge cases (connection drops, etc.)
- [ ] Add error notifications
- [ ] Performance optimization

## Testing the Discovery Feature

### 1. PC Side Test
```csharp
// In your PC app
var config = new ServerConfig();
var discoveryService = new UdpDiscoveryService(config);
await discoveryService.StartListeningAsync(5001);

Console.WriteLine("Discovery service running...");
// Should see "[Discovery] UDP listener started successfully" in debug output
```

### 2. Mobile Side Test
```csharp
// In your mobile app
var discoveryService = new UdpDiscoveryService();
var servers = await discoveryService.DiscoverServersAsync();

Console.WriteLine($"Found {servers.Count} server(s)");
foreach (var server in servers)
{
    Console.WriteLine($"  - {server.Name} at {server.IpAddress}:{server.Port}");
}
```

### 3. Full UI Test
1. Create PC app with discovery service
2. Create mobile app with ConnectionView
3. Run PC app
4. Run mobile app
5. Tap "Discover Servers" button
6. Should see PC server in list

## Configuration

### Basic Configuration
```json
{
  "server": {
    "port": 5000,
    "discoveryPort": 5001,
    "enableDiscovery": true
  }
}
```

### Advanced Configuration
```json
{
  "server": {
    "port": 5000,
    "discoveryPort": 5001,
    "autoStart": true,
    "enableDiscovery": true
  },
  "buttons": [
    {"id": "shieldboost", "key": "F1", "icon": "shield", "color": "#4CAF50"},
    {"id": "ecm", "key": "F2", "icon": "flash", "color": "#2196F3"}
  ]
}
```

## Code Quality Features

All code follows your requirements:
- ✅ **Null checking** on all function parameters
- ✅ **Early returns** to avoid nested if statements
- ✅ **Allman-style braces** for C#
- ✅ **Debug logging** at entry/exit and intermediate steps
- ✅ **No simplified versions** - full implementation

## Dependencies Required

### PC Project (EDSC.Desktop)
```xml
<PackageReference Include="Avalonia" Version="11.0.0" />
<PackageReference Include="Avalonia.Desktop" Version="11.0.0" />
```

### Mobile Project (EDSC.Android)
```xml
<PackageReference Include="Avalonia.Android" Version="11.0.0" />
```

### Shared Project (EDSC)
```xml
<PackageReference Include="System.Text.Json" Version="8.0.0" />
```

**Note:** `System.Net.Sockets` is built-in to .NET, no package needed.

## Troubleshooting

### Discovery Not Working?
1. **Check firewall**: Allow UDP port 5001
2. **Same network**: Both devices on same WiFi
3. **Check logs**: Look for "[Discovery]" in debug output
4. **Try manual**: Use manual IP entry to verify network connectivity

### Common Issues
- **"No servers found"**: PC not running or firewall blocking
- **"Discovery timeout"**: Network not allowing broadcasts
- **"Wrong IP address"**: Multiple network adapters - modify GetLocalIpAddress()

## Support Files

- **DISCOVERY_INTEGRATION.md** - Detailed integration guide with examples
- **config.example.json** - Example configuration file
- **App.axaml.cs.example** - PC integration example
- **MainActivity.cs.example** - Mobile integration example

## Contact

For questions or issues, check the debug logs first - they're very detailed!

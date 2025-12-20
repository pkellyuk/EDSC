# EDSC - Network Discovery Integration Guide

## Overview
This guide explains how to integrate the automatic UDP broadcast discovery feature into your EDSC application.

## Architecture

### Discovery Flow
1. **PC Server** starts and listens on UDP port 5001 for discovery requests
2. **Mobile Client** broadcasts discovery requests to subnet (255.255.255.255:5001)
3. **PC Server** responds with its IP address, port, and server name
4. **Mobile Client** displays discovered servers or allows manual IP entry
5. User connects to selected server via HTTP

## Files Created

### Core Discovery Components
- `src/EDSC/Models/Discovery/DiscoveryMessage.cs` - Request/Response models
- `src/EDSC/Models/Discovery/DiscoveredServer.cs` - Server representation
- `src/EDSC/Services/Discovery/IDiscoveryService.cs` - Service interface
- `src/EDSC/Services/Discovery/UdpDiscoveryService.PC.cs` - PC implementation
- `src/EDSC/Services/Discovery/UdpDiscoveryService.Android.cs` - Mobile implementation

### UI Components
- `src/EDSC/ViewModels/ConnectionViewModel.cs` - Connection logic
- `src/EDSC/Views/ConnectionView.axaml` - Mobile connection UI
- `src/EDSC/Views/ConnectionView.axaml.cs` - Code-behind

### Configuration
- `src/EDSC/Models/ServerConfig.cs` - Server configuration model

### Integration Examples
- `src/EDSC.Desktop/App.axaml.cs.example` - PC app integration
- `src/EDSC.Android/MainActivity.cs.example` - Android app integration

## PC Server Integration

### 1. Initialize Discovery Service

```csharp
using EDSC.Services.Discovery;
using EDSC.Models;

// In your App.axaml.cs or main application class
private IDiscoveryService _discoveryService;
private ServerConfig _serverConfig;

public override async void OnFrameworkInitializationCompleted()
{
    // Load configuration
    _serverConfig = new ServerConfig
    {
        Port = 5000,              // HTTP port
        DiscoveryPort = 5001,     // UDP discovery port
        AutoStart = true,
        EnableDiscovery = true
    };

    // Initialize discovery service
    _discoveryService = new UdpDiscoveryService(_serverConfig);

    // Start listening for discovery requests
    if (_serverConfig.EnableDiscovery)
    {
        await _discoveryService.StartListeningAsync(_serverConfig.DiscoveryPort);
    }

    base.OnFrameworkInitializationCompleted();
}
```

### 2. Stop Discovery on Exit

```csharp
public override async void OnExit()
{
    // Stop discovery service
    if (_discoveryService != null && _discoveryService.IsRunning)
    {
        await _discoveryService.StopListeningAsync();
    }

    base.OnExit();
}
```

## Mobile Client Integration

### 1. Initialize Discovery Service

```csharp
using EDSC.Services.Discovery;
using EDSC.ViewModels;

// In your MainActivity or App class
private IDiscoveryService _discoveryService;
private ConnectionViewModel _connectionViewModel;

protected override void OnCreate(Bundle savedInstanceState)
{
    base.OnCreate(savedInstanceState);

    // Initialize discovery service (mobile version)
    _discoveryService = new UdpDiscoveryService();

    // Initialize connection view model
    _connectionViewModel = new ConnectionViewModel(_discoveryService);

    // Optional: Auto-discover on startup
    _ = _connectionViewModel.DiscoverServersAsync();
}
```

### 2. Display Connection View

```csharp
// Set ConnectionView as initial view
var connectionView = new ConnectionView
{
    DataContext = _connectionViewModel
};

// Add to your view hierarchy
```

### 3. Handle Connection

The `ConnectionViewModel` will:
- Discover servers automatically
- Display them in a list
- Allow manual IP entry as fallback
- Trigger connection when user selects a server

## Configuration File Format

Create a `config.json` file:

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
      "icon": "shield",
      "color": "#4CAF50"
    },
    {
      "id": "ecm",
      "key": "F2",
      "icon": "flash",
      "color": "#2196F3"
    }
  ]
}
```

## Network Requirements

### Firewall Rules (Windows PC)
You need to allow:
- **Inbound UDP** on port 5001 (discovery)
- **Inbound TCP** on port 5000 (HTTP commands)

### PowerShell Commands (Run as Administrator)
```powershell
# Allow UDP discovery
New-NetFirewallRule -DisplayName "EDSC Discovery" -Direction Inbound -Protocol UDP -LocalPort 5001 -Action Allow

# Allow HTTP commands
New-NetFirewallRule -DisplayName "EDSC HTTP" -Direction Inbound -Protocol TCP -LocalPort 5000 -Action Allow
```

### Network Setup
- Both PC and mobile must be on **same WiFi network**
- Router must allow UDP broadcast (most do by default)
- Corporate networks may block broadcasts - use manual entry

## Testing

### Unit Tests
```csharp
[Test]
public void DiscoveryRequest_Serializes_Correctly()
{
    var request = new DiscoveryRequest
    {
        RequestId = "test-123",
        Timestamp = 1234567890
    };

    var json = JsonSerializer.Serialize(request);
    var deserialized = JsonSerializer.Deserialize<DiscoveryRequest>(json);

    Assert.AreEqual("discover", deserialized.Type);
    Assert.AreEqual("test-123", deserialized.RequestId);
}
```

### Manual Testing
1. Start PC application (should see "Discovery service started" in logs)
2. Start mobile application
3. Tap "Discover Servers" button
4. Verify PC server appears in list
5. Select server and tap "Connect"

### Debug Logging
All discovery operations log to Debug output:
- PC: Look for `[Discovery]` tags in Output window
- Mobile: Check Logcat with tag filter `Discovery`

## Troubleshooting

### No Servers Found

**Check:**
- Both devices on same WiFi network
- Firewall allows UDP port 5001
- PC discovery service is running (`IsRunning = true`)
- Mobile can ping PC's IP address

**Solution:**
- Use manual IP entry as fallback
- Check Windows Firewall settings
- Verify network allows broadcast packets

### Discovery Works But Connection Fails

**Check:**
- HTTP port 5000 is open in firewall
- HTTP server is actually running on PC
- IP address in response matches PC's network IP (not 127.0.0.1)

**Solution:**
- Verify `GetLocalIpAddress()` returns correct IP
- Test HTTP connection manually (e.g., with browser)

### Multiple Network Adapters

If PC has multiple network adapters (WiFi + Ethernet + VPN), `GetLocalIpAddress()` might return wrong IP.

**Solution:**
Modify `GetLocalIpAddress()` to select specific adapter:
```csharp
private string GetLocalIpAddress()
{
    var host = Dns.GetHostEntry(Dns.GetHostName());

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

    // Fallback to first IPv4
    // ... rest of logic
}
```

## Security Considerations

### Current Implementation
- **No authentication** on discovery (by design for simplicity)
- Discovery only works on local subnet (not internet-routable)
- Anyone on network can see your server

### Future Enhancements
- Add shared secret/passphrase for discovery response
- Implement certificate pinning for HTTP connection
- Add rate limiting to prevent discovery spam
- Support mDNS for better discovery

## Advanced Usage

### Disable Discovery
Set `EnableDiscovery = false` in configuration to disable automatic discovery:

```json
{
  "server": {
    "enableDiscovery": false
  }
}
```

Users must enter IP manually.

### Custom Discovery Port
Change discovery port (if 5001 conflicts):

```json
{
  "server": {
    "discoveryPort": 5002
  }
}
```

**Note:** Mobile app broadcasts to port in `DISCOVERY_PORT` constant - keep them synced.

### Discovery Timeout
Mobile discovery retries 3 times with 1-second timeout (total ~3 seconds).

Modify in `UdpDiscoveryService.Android.cs`:
```csharp
private const int RETRY_COUNT = 3;    // Number of attempts
private const int TIMEOUT_MS = 1000;  // Milliseconds per attempt
```

## Next Steps

After implementing discovery:
1. Implement HTTP command server on PC
2. Implement HTTP client on mobile
3. Add button configuration loading
4. Implement keyboard simulation (PC)
5. Add system tray integration (PC)
6. Implement connection persistence (mobile)

## Support

For issues or questions:
- Check debug logs for `[Discovery]` messages
- Verify network connectivity
- Test manual connection first
- File issue with logs attached

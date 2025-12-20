# EDSC - API Documentation

Complete API reference for the EDSC HTTP command server and UDP discovery protocol.

## Table of Contents

- [Overview](#overview)
- [HTTP Command API](#http-command-api)
- [UDP Discovery Protocol](#udp-discovery-protocol)
- [Error Handling](#error-handling)
- [Examples](#examples)
- [Client Libraries](#client-libraries)

## Overview

EDSC uses two protocols:

1. **HTTP API** (TCP Port 5000) - For sending button commands
2. **UDP Discovery** (UDP Port 5001) - For automatic server discovery

### Base URL

```
http://<SERVER_IP>:5000
```

Default: `http://localhost:5000` (when running locally)

### Content Type

All HTTP requests and responses use:
```
Content-Type: application/json
```

## HTTP Command API

### Endpoints

- `GET /` - Health check
- `POST /command` - Send button command

---

### GET / - Health Check

Check if server is running and responsive.

#### Request

```http
GET / HTTP/1.1
Host: localhost:5000
```

#### Response (200 OK)

```json
{
  "service": "EDSC",
  "status": "running",
  "version": "1.0.0"
}
```

#### cURL Example

```bash
curl http://localhost:5000/
```

#### Response Fields

| Field | Type | Description |
|-------|------|-------------|
| `service` | string | Always "EDSC" |
| `status` | string | Always "running" when server is up |
| `version` | string | Server version number |

---

### POST /command - Send Button Command

Send a keyboard command to be simulated.

#### Request

```http
POST /command HTTP/1.1
Host: localhost:5000
Content-Type: application/json

{
  "buttonId": "shieldboost",
  "key": "F1",
  "timestamp": 1234567890
}
```

#### Request Body

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `buttonId` | string | Yes | Unique identifier for the button |
| `key` | string | Yes | Keyboard key to press (see [Supported Keys](#supported-keys)) |
| `timestamp` | number | Yes | Unix timestamp (seconds since epoch) |

#### Success Response (200 OK)

```json
{
  "success": true,
  "message": "Key 'F1' pressed",
  "timestamp": 1234567890
}
```

#### Error Response (400 Bad Request)

```json
{
  "success": false,
  "message": "Key is required",
  "timestamp": 1234567890
}
```

#### Response Fields

| Field | Type | Description |
|-------|------|-------------|
| `success` | boolean | `true` if command succeeded, `false` otherwise |
| `message` | string | Human-readable result message |
| `timestamp` | number | Server timestamp when response was generated |

#### cURL Example

```bash
curl -X POST http://localhost:5000/command \
  -H "Content-Type: application/json" \
  -d '{
    "buttonId": "shieldboost",
    "key": "F1",
    "timestamp": 1234567890
  }'
```

#### PowerShell Example

```powershell
$body = @{
    buttonId = "shieldboost"
    key = "F1"
    timestamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
} | ConvertTo-Json

Invoke-RestMethod -Uri "http://localhost:5000/command" `
  -Method Post `
  -Body $body `
  -ContentType "application/json"
```

#### JavaScript/TypeScript Example

```typescript
async function sendCommand(buttonId: string, key: string) {
  const response = await fetch('http://localhost:5000/command', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({
      buttonId,
      key,
      timestamp: Math.floor(Date.now() / 1000),
    }),
  });

  return await response.json();
}

// Usage
const result = await sendCommand('shieldboost', 'F1');
console.log(result.message); // "Key 'F1' pressed"
```

#### Python Example

```python
import requests
import time

def send_command(button_id: str, key: str) -> dict:
    url = "http://localhost:5000/command"
    payload = {
        "buttonId": button_id,
        "key": key,
        "timestamp": int(time.time())
    }
    response = requests.post(url, json=payload)
    return response.json()

# Usage
result = send_command("shieldboost", "F1")
print(result["message"])  # "Key 'F1' pressed"
```

### Supported Keys

The following key names are supported:

#### Function Keys
```
F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12
```

#### Letters (Case Insensitive)
```
A, B, C, D, E, F, G, H, I, J, K, L, M,
N, O, P, Q, R, S, T, U, V, W, X, Y, Z
```

#### Numbers
```
0, 1, 2, 3, 4, 5, 6, 7, 8, 9
```

#### Number Pad
```
NUMPAD0, NUMPAD1, NUMPAD2, NUMPAD3, NUMPAD4,
NUMPAD5, NUMPAD6, NUMPAD7, NUMPAD8, NUMPAD9
```

#### Special Keys
```
ESCAPE, ESC         - Escape key
ENTER, RETURN       - Enter/Return key
SPACE, SPACEBAR     - Space bar
TAB                 - Tab key
BACKSPACE           - Backspace key
DELETE              - Delete key
INSERT              - Insert key
```

#### Modifier Keys
```
SHIFT               - Shift key
CONTROL, CTRL       - Control key
ALT                 - Alt key
```

#### Navigation Keys
```
UP, DOWN, LEFT, RIGHT - Arrow keys
HOME, END             - Home/End keys
PAGEUP, PAGEDOWN      - Page Up/Down keys
```

#### Key Combination Example

For key combinations, send individual keys in sequence:

```bash
# Ctrl+C (Copy)
curl -X POST http://localhost:5000/command -d '{"buttonId":"copy","key":"CONTROL",...}'
curl -X POST http://localhost:5000/command -d '{"buttonId":"copy","key":"C",...}'
```

**Note**: The server sends single keypresses. For combinations, client should send multiple requests or server should be enhanced to support key combinations.

## UDP Discovery Protocol

### Overview

The discovery protocol uses UDP broadcast to allow mobile clients to automatically find PC servers on the local network.

- **Port**: 5001 (UDP)
- **Broadcast Address**: 255.255.255.255
- **Protocol**: JSON over UDP

### Discovery Request (Client → Server)

Mobile client broadcasts this message to find servers.

#### Packet Structure

```json
{
  "type": "discover",
  "requestId": "550e8400-e29b-41d4-a716-446655440000",
  "timestamp": 1234567890
}
```

#### Fields

| Field | Type | Description |
|-------|------|-------------|
| `type` | string | Always "discover" |
| `requestId` | string | Unique UUID to match response |
| `timestamp` | number | Unix timestamp (seconds) |

#### Send Broadcast (Example)

```python
import socket
import json
import time
import uuid

def discover_servers():
    # Create UDP socket
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)
    sock.settimeout(1)  # 1 second timeout

    # Prepare request
    request = {
        "type": "discover",
        "requestId": str(uuid.uuid4()),
        "timestamp": int(time.time())
    }

    # Send broadcast
    message = json.dumps(request).encode('utf-8')
    sock.sendto(message, ('255.255.255.255', 5001))

    # Listen for responses
    servers = []
    try:
        while True:
            data, addr = sock.recvfrom(1024)
            response = json.loads(data.decode('utf-8'))
            servers.append(response)
    except socket.timeout:
        pass

    sock.close()
    return servers

# Usage
servers = discover_servers()
for server in servers:
    print(f"Found: {server['serverName']} at {server['ipAddress']}")
```

### Discovery Response (Server → Client)

PC server responds with its details.

#### Packet Structure

```json
{
  "type": "response",
  "requestId": "550e8400-e29b-41d4-a716-446655440000",
  "serverName": "EDSC-DESKTOP-ABC123",
  "ipAddress": "192.168.1.100",
  "httpPort": 5000,
  "version": "1.0.0"
}
```

#### Fields

| Field | Type | Description |
|-------|------|-------------|
| `type` | string | Always "response" |
| `requestId` | string | Matches request UUID |
| `serverName` | string | Server name (format: "EDSC-{ComputerName}") |
| `ipAddress` | string | Server's IPv4 address |
| `httpPort` | number | HTTP API port number |
| `version` | string | Server version |

### Discovery Flow Diagram

```
Mobile Client                           PC Server
     |                                      |
     |  UDP Broadcast (port 5001)          |
     |  {"type":"discover",...}            |
     |------------------------------------->|
     |                                      |
     |                                      | (Receives request)
     |                                      | (Validates request)
     |                                      |
     |  UDP Response                        |
     |  {"type":"response",...}             |
     |<-------------------------------------|
     |                                      |
     | (Displays server in UI)              |
     |                                      |
     |  HTTP GET /                          |
     |------------------------------------->|
     |                                      |
     |  {"service":"EDSC",...}              |
     |<-------------------------------------|
     |                                      |
     | (Connection established)             |
```

### Discovery Retry Logic

Recommended retry strategy:

```typescript
async function discoverServers(retries = 3, timeout = 1000): Promise<Server[]> {
  const servers = new Set<Server>();

  for (let i = 0; i < retries; i++) {
    // Send broadcast
    await sendDiscoveryRequest();

    // Wait for responses
    await new Promise(resolve => setTimeout(resolve, timeout));

    // Small delay between retries
    if (i < retries - 1) {
      await new Promise(resolve => setTimeout(resolve, 200));
    }
  }

  return Array.from(servers);
}
```

## Error Handling

### HTTP Status Codes

| Code | Meaning | When It Occurs |
|------|---------|----------------|
| 200 | OK | Command succeeded |
| 400 | Bad Request | Invalid request (missing/invalid fields) |
| 404 | Not Found | Invalid endpoint |
| 500 | Internal Server Error | Server-side error |

### Error Response Format

All error responses follow this format:

```json
{
  "success": false,
  "message": "Error description here",
  "timestamp": 1234567890
}
```

### Common Errors

#### Missing Key Field

**Request:**
```json
{
  "buttonId": "test"
  // Missing "key" field
}
```

**Response (400):**
```json
{
  "success": false,
  "message": "Key is required",
  "timestamp": 1234567890
}
```

#### Invalid Key Name

**Request:**
```json
{
  "buttonId": "test",
  "key": "INVALID_KEY_NAME"
}
```

**Response (200):** *(Still returns 200 but logs error internally)*
```json
{
  "success": true,
  "message": "Key 'INVALID_KEY_NAME' pressed",
  "timestamp": 1234567890
}
```

Note: Invalid keys are logged but don't cause HTTP errors. Check server logs for "Failed to parse key" messages.

#### Network Timeout

Client-side timeout when server is unreachable:

```python
try:
    response = requests.post(url, json=payload, timeout=5)
except requests.Timeout:
    print("Server did not respond in time")
except requests.ConnectionError:
    print("Could not connect to server")
```

## Examples

### Complete Workflow Example

```python
import requests
import socket
import json
import time
import uuid

class EDSCClient:
    def __init__(self):
        self.server_ip = None
        self.server_port = 5000

    def discover(self):
        """Discover EDSC servers on network"""
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        sock.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)
        sock.settimeout(1)

        request = {
            "type": "discover",
            "requestId": str(uuid.uuid4()),
            "timestamp": int(time.time())
        }

        message = json.dumps(request).encode('utf-8')
        sock.sendto(message, ('255.255.255.255', 5001))

        try:
            data, addr = sock.recvfrom(1024)
            response = json.loads(data.decode('utf-8'))
            self.server_ip = response['ipAddress']
            self.server_port = response['httpPort']
            return response
        except socket.timeout:
            return None
        finally:
            sock.close()

    def test_connection(self):
        """Test HTTP connection to server"""
        if not self.server_ip:
            raise Exception("No server discovered")

        url = f"http://{self.server_ip}:{self.server_port}/"
        response = requests.get(url, timeout=5)
        return response.json()

    def send_command(self, button_id, key):
        """Send button command to server"""
        if not self.server_ip:
            raise Exception("No server discovered")

        url = f"http://{self.server_ip}:{self.server_port}/command"
        payload = {
            "buttonId": button_id,
            "key": key,
            "timestamp": int(time.time())
        }

        response = requests.post(url, json=payload, timeout=5)
        return response.json()

# Usage
client = EDSCClient()

# 1. Discover server
server = client.discover()
if server:
    print(f"Found server: {server['serverName']}")

    # 2. Test connection
    health = client.test_connection()
    print(f"Server status: {health['status']}")

    # 3. Send commands
    result = client.send_command("shieldboost", "F1")
    print(f"Command result: {result['message']}")
else:
    print("No server found")
```

### Rate Limiting Consideration

When sending rapid commands, add delay to avoid overwhelming the server:

```python
import time

def send_commands_safely(client, commands, delay=0.1):
    """Send multiple commands with rate limiting"""
    for button_id, key in commands:
        result = client.send_command(button_id, key)
        print(f"{button_id}: {result['message']}")
        time.sleep(delay)  # 100ms delay between commands

# Usage
commands = [
    ("shield", "F1"),
    ("chaff", "F2"),
    ("heatsink", "F3"),
]
send_commands_safely(client, commands)
```

## Client Libraries

### C# (EDSC Native)

```csharp
using EDSC.Services;

// Discovery
var discoveryService = new UdpDiscoveryServiceAndroid();
var servers = await discoveryService.DiscoverServersAsync();

// Command
var commandClient = new HttpCommandClient();
commandClient.SetServerAddress("192.168.1.100", 5000);
var response = await commandClient.SendCommandAsync("shieldboost", "F1");
```

### TypeScript/JavaScript

```typescript
class EDSCClient {
  constructor(private serverUrl: string) {}

  async sendCommand(buttonId: string, key: string) {
    const response = await fetch(`${this.serverUrl}/command`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        buttonId,
        key,
        timestamp: Math.floor(Date.now() / 1000),
      }),
    });

    return await response.json();
  }
}

const client = new EDSCClient('http://192.168.1.100:5000');
await client.sendCommand('shieldboost', 'F1');
```

## Security Considerations

⚠️ **Important**: This API has NO authentication or encryption.

- **Use only on trusted networks** (home WiFi)
- **Do not expose to internet** (no port forwarding)
- **Consider VPN** for remote access
- **Future**: Add API key authentication, HTTPS

## Support

For API issues:
- Check server logs for detailed error messages
- Verify network connectivity
- Test with `curl` before using client code
- File issues on GitHub with request/response examples

---

**API Version**: 1.0.0
**Last Updated**: December 2025

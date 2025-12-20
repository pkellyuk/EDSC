# EDSC - Elite Dangerous Ship Controls

## Overview
A unified C# AvaloniaUI application that provides external control for Elite Dangerous ship functions (ECM, Chaff, Shield Booster, etc.) with a single codebase for both PC and Android platforms.

## Architecture

### System Design
```
┌───────────────────────────────────────────────────────────────────────────────┐
│                     EDSC - Unified Architecture                             │
├─────────────────┬─────────────────┬─────────────────┬─────────────────┬─────┤
│  Shared Code   │  Platform       │  PC-Specific    │  Mobile-        │ UI  │
│  (Avalonia)     │  Abstraction    │  Features       │  Specific       │     │
│  (Buttons,      │  Layer          │  (Keyboard,     │  Features      │     │
│   Config)       │                 │   Server)      │  (Remote Ctrl) │     │
└─────────────────┴─────────────────┴─────────────────┴─────────────────┴─────┘
```

## Core Features

### PC Version (Desktop)
- **Local Server**: HTTP endpoint for receiving commands
- **Keyboard Simulation**: Sends key presses to Elite Dangerous
- **Configuration Editor**: GUI for button mappings
- **System Tray**: Minimize to tray while running
- **Auto-start**: Optional startup with Windows

### Mobile Version (Android)
- **Remote Control**: Connects to PC via WiFi
- **Touch Optimization**: Large buttons, scrollable lists
- **Connection Manager**: Auto-discover PC on network
- **Battery Optimization**: Efficient HTTP communication
- **Status Display**: Shows connection and last action

### Shared Features
- **Button Customization**: Icons, colors, labels
- **Configuration Sync**: Shared JSON settings
- **Cross-Platform UI**: Consistent experience
- **Error Handling**: Graceful failure modes

## Technical Implementation

### Technology Stack
- **Framework**: AvaloniaUI (.NET 6+)
- **PC Keyboard**: Windows.Input.Simulator
- **Networking**: HttpClient (mobile), Kestrel (PC)
- **Configuration**: JSON files
- **Icons**: MaterialIcons
- **Build**: MSBuild with platform targets

### Platform Detection
```csharp
#if ANDROID
// Mobile-specific code
#elif DESKTOP
// PC-specific code
#endif
```

### Configuration Format
```json
{
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
  ],
  "server": {
    "port": 5000,
    "autoStart": true
  }
}
```

## Implementation Plan

### Phase 1: Shared Codebase (4-5 days)
- [ ] Create AvaloniaUI project with platform detection
- [ ] Implement button layout and view models
- [ ] Develop shared configuration system
- [ ] Create network communication layer
- [ ] Add icon support

### Phase 2: PC Features (3-4 days)
- [ ] Implement keyboard simulation
- [ ] Create local HTTP server with Kestrel
- [ ] Build configuration editor GUI
- [ ] Add system tray integration
- [ ] Implement auto-start functionality

### Phase 3: Mobile Features (3-4 days)
- [ ] Implement remote control protocol
- [ ] Create connection manager with auto-discovery
- [ ] Optimize for touch input
- [ ] Add battery saving features
- [ ] Implement status feedback

### Phase 4: Integration (2 days)
- [ ] Test PC ↔ Mobile communication
- [ ] Implement configuration sync
- [ ] Handle connection drops gracefully
- [ ] Performance optimization
- [ ] Final testing

## Project Timeline
- **Total Duration**: ~14-16 days
- **Milestone 1**: Shared codebase complete (5 days)
- **Milestone 2**: PC version functional (9 days)
- **Milestone 3**: Mobile version complete (13 days)
- **Milestone 4**: Final testing and polish (16 days)

## Future Enhancements

### Short Term (Post-MVP)
- [ ] Auto-discovery using mDNS
- [ ] Status monitoring (show game status)
- [ ] Multiple button presets
- [ ] Voice command support

### Long Term
- [ ] Web version for browser access
- [ ] Cloud sync for configurations
- [ ] Advanced scripting
- [ ] Plugin system for custom modules

## Technical Challenges & Solutions

| Challenge | Solution |
|------------|----------|
| **Platform differences** | Conditional compilation with `#if` directives |
| **Keyboard focus** | Windows API for active window detection |
| **Network reliability** | HTTP with retry logic and timeout handling |
| **Configuration sync** | Manual sync with version checking |
| **Battery usage** | Efficient HTTP polling and background mode |

## Deployment

### PC Version
1. Build as self-contained executable
2. Include configuration file
3. Optional: Create installer with auto-start

### Mobile Version
1. Build Android APK
2. Publish to Google Play or sideload
3. Requires same network as PC

## Configuration Examples

### Basic Setup
```json
{
  "buttons": [
    {"id": "shieldboost", "key": "F1", "icon": "shield"},
    {"id": "ecm", "key": "F2", "icon": "flash"},
    {"id": "chaff", "key": "F3", "icon": "smoke"},
    {"id": "fuelscoop", "key": "F4", "icon": "gas"}
  ],
  "server": {"port": 5000, "autoStart": true}
}
```

### Advanced Setup with Colors
```json
{
  "buttons": [
    {
      "id": "shieldboost",
      "key": "F1",
      "icon": "shield",
      "color": "#4CAF50",
      "size": 80
    },
    {
      "id": "ecm",
      "key": "F2",
      "icon": "flash",
      "color": "#2196F3",
      "size": 80
    }
  ]
}
```

## Code Structure

```
src/
├── EDSC/
│   ├── Views/          # UI components
│   │   ├── MainView.axaml
│   │   ├── SettingsView.axaml
│   │   └── ButtonView.axaml
│   ├── ViewModels/     # MVVM models
│   │   ├── MainViewModel.cs
│   │   ├── ButtonViewModel.cs
│   │   └── SettingsViewModel.cs
│   ├── Services/       # Platform-specific services
│   │   ├── IKeyboardService.cs
│   │   ├── KeyboardService.PC.cs
│   │   └── KeyboardService.Android.cs
│   ├── Models/         # Data models
│   │   ├── ButtonConfig.cs
│   │   └── ServerConfig.cs
│   ├── Network/        # Network communication
│   │   ├── ICommandSender.cs
│   │   ├── HttpCommandSender.cs
│   │   └── LocalCommandSender.cs
│   └── App.axaml.cs    # Application entry point
└── EDSC.Android/      # Android-specific code
└── EDSC.Desktop/      # Desktop-specific code
```

## Testing Strategy

### Unit Tests
- [ ] Button configuration loading
- [ ] Network communication
- [ ] Platform detection

### Integration Tests
- [ ] PC ↔ Mobile communication
- [ ] Keyboard simulation
- [ ] Configuration sync

### Manual Tests
- [ ] Elite Dangerous integration
- [ ] Multiple device scenarios
- [ ] Network conditions (WiFi, mobile data)

## Documentation

### User Guide
1. Install PC version
2. Configure button mappings
3. Install mobile version
4. Connect to same network
5. Control Elite Dangerous remotely

### Troubleshooting
- **Connection issues**: Check firewall, same network
- **Key not working**: Verify Elite Dangerous key bindings
- **App not responding**: Restart both devices

## License
MIT License - see LICENSE file for details

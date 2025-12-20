# EDSC - Build Guide

Complete instructions for building and deploying EDSC from source.

## Table of Contents

- [Prerequisites](#prerequisites)
- [Build Environment Setup](#build-environment-setup)
- [Building the Solution](#building-the-solution)
- [Project-Specific Builds](#project-specific-builds)
- [Publishing](#publishing)
- [Deployment](#deployment)
- [Common Issues](#common-issues)

## Prerequisites

### Required Software

1. **.NET 8.0 SDK** (or later)
   - Download: https://dotnet.microsoft.com/download/dotnet/8.0
   - Verify installation: `dotnet --version`
   - Should output: `8.0.x` or higher

2. **Git** (for cloning repository)
   - Download: https://git-scm.com/downloads
   - Verify: `git --version`

3. **IDE** (Choose one)
   - **Visual Studio 2022** (Windows, Community Edition or higher)
     - Workloads: .NET Desktop Development, Mobile Development with .NET
   - **JetBrains Rider** (Cross-platform, commercial/trial)
   - **VS Code** with C# extension (Free, all platforms)

### Android Development (For Mobile App Only)

4. **Android SDK**
   - Install via Visual Studio Installer (Mobile development with .NET workload)
   - Or standalone: https://developer.android.com/studio

5. **Java Development Kit (JDK) 11+**
   - Required for Android build tools
   - OpenJDK: https://adoptium.net/

## Build Environment Setup

### 1. Clone Repository

```bash
git clone https://github.com/yourusername/EDSC.git
cd EDSC
```

### 2. Verify .NET Installation

```bash
dotnet --version
# Should output: 8.0.x or higher

dotnet --list-sdks
# Should show .NET 8.0.x in the list
```

### 3. Restore NuGet Packages

```bash
# From solution root
dotnet restore

# Or restore all projects
dotnet restore EDSC.sln
```

Expected output:
```
Determining projects to restore...
Restored C:\path\to\EDSC\src\EDSC\EDSC.csproj
Restored C:\path\to\EDSC\src\EDSC.Desktop\EDSC.Desktop.csproj
Restored C:\path\to\EDSC\src\EDSC.Android\EDSC.Android.csproj
```

## Building the Solution

### Option 1: Build Everything

```bash
# From solution root
dotnet build

# Or with configuration
dotnet build -c Release
```

This builds:
- ✅ EDSC (shared library)
- ✅ EDSC.Desktop (PC server)
- ⚠️ EDSC.Android (requires Android SDK)

### Option 2: Build in Visual Studio

1. Open `EDSC.sln`
2. Select configuration (Debug/Release)
3. Build > Build Solution (Ctrl+Shift+B)

### Option 3: Build in Rider

1. Open `EDSC.sln`
2. Select configuration
3. Build > Build All

## Project-Specific Builds

### EDSC (Shared Library)

```bash
cd src/EDSC
dotnet build
```

**Output**: `bin/Debug/net8.0/EDSC.dll`

This library contains:
- Models (ButtonConfig, ServerConfig, CommandRequest, etc.)
- Services (Discovery, Configuration, HTTP Client)
- ViewModels (ConnectionViewModel, MainViewModel)
- Views (ConnectionView, MainView)

### EDSC.Desktop (PC Server)

```bash
cd src/EDSC.Desktop
dotnet build
```

**Output**: `bin/Debug/net8.0-windows/EDSC.Desktop.exe`

**Dependencies**:
- Avalonia.Desktop 11.0.10
- InputSimulatorCore 1.0.5
- Microsoft.AspNetCore.App (FrameworkReference)

**Run directly after build**:
```bash
dotnet run
```

**Or run the executable**:
```bash
cd bin/Debug/net8.0-windows
.\EDSC.Desktop.exe
```

### EDSC.Android (Mobile App)

**Prerequisites**:
- Android SDK installed
- Android device or emulator configured

```bash
cd src/EDSC.Android
dotnet build
```

**Output**: `bin/Debug/net8.0-android/com.edsc.app-Signed.apk`

**Deploy to device**:
```bash
# List connected devices
adb devices

# Install APK
adb install bin/Debug/net8.0-android/com.edsc.app-Signed.apk
```

**Or use Visual Studio**:
1. Set EDSC.Android as startup project
2. Select Android device/emulator
3. Press F5 to build and deploy

## Publishing

### Publish PC Server (Standalone Executable)

**Self-contained (includes .NET runtime)**:
```bash
cd src/EDSC.Desktop

# Windows x64
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

# Output: bin/Release/net8.0-windows/win-x64/publish/EDSC.Desktop.exe
```

**Framework-dependent (requires .NET 8.0 on target machine)**:
```bash
dotnet publish -c Release -r win-x64 --self-contained false

# Smaller size but needs .NET 8.0 installed
```

**Publish Flags Explained**:
- `-c Release` - Release configuration (optimized)
- `-r win-x64` - Target runtime (Windows 64-bit)
- `--self-contained true` - Include .NET runtime
- `-p:PublishSingleFile=true` - Single executable file
- `-p:PublishTrimmed=true` - Trim unused code (smaller size)

### Publish Android App (Release APK)

```bash
cd src/EDSC.Android

# Build release APK
dotnet publish -c Release -f net8.0-android
```

**Sign the APK**:
```bash
# Generate keystore (first time only)
keytool -genkey -v -keystore edsc.keystore -alias edsc -keyalg RSA -keysize 2048 -validity 10000

# Sign APK
jarsigner -verbose -sigalg SHA1withRSA -digestalg SHA1 -keystore edsc.keystore \
  bin/Release/net8.0-android/com.edsc.app.apk edsc

# Align APK (optimize)
zipalign -v 4 bin/Release/net8.0-android/com.edsc.app.apk \
  bin/Release/net8.0-android/com.edsc.app-aligned.apk
```

**Or configure in project file** (`EDSC.Android.csproj`):
```xml
<PropertyGroup Condition="'$(Configuration)' == 'Release'">
  <AndroidKeyStore>true</AndroidKeyStore>
  <AndroidSigningKeyStore>path\to\edsc.keystore</AndroidSigningKeyStore>
  <AndroidSigningKeyAlias>edsc</AndroidSigningKeyAlias>
  <AndroidSigningKeyPass>your-password</AndroidSigningKeyPass>
  <AndroidSigningStorePass>your-password</AndroidSigningStorePass>
</PropertyGroup>
```

## Deployment

### PC Server Deployment

**Option 1: Copy Published Files**

1. Publish as shown above
2. Copy entire `publish/` folder to target PC
3. Create `config.json` alongside executable
4. Run `EDSC.Desktop.exe`

**Option 2: Create Installer (Advanced)**

Use tools like:
- **WiX Toolset** - Create MSI installer
- **Inno Setup** - Create setup.exe
- **ClickOnce** - Web-deployed application

**Example folder structure after deployment**:
```
C:\Program Files\EDSC\
├── EDSC.Desktop.exe
├── config.json
├── (other DLL files if not single-file)
└── README.txt
```

### Android App Deployment

**Option 1: Direct Install (Development)**

```bash
# Connect device via USB
adb install bin/Release/net8.0-android/com.edsc.app-Signed.apk
```

**Option 2: Sideload (No Play Store)**

1. Copy APK to device
2. Enable "Install from Unknown Sources" in Android settings
3. Open APK file on device
4. Follow installation prompts

**Option 3: Google Play Store (Production)**

1. Sign APK with production keystore
2. Create app listing in Google Play Console
3. Upload signed APK/AAB
4. Submit for review

## Configuration After Build

### PC Server Configuration

Create `config.json` in executable directory:

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
    }
  ]
}
```

See `config.example.json` for complete example.

### Windows Firewall Configuration

After deployment, configure firewall:

```powershell
# Run as Administrator
New-NetFirewallRule -DisplayName "EDSC Discovery" `
  -Direction Inbound -Protocol UDP -LocalPort 5001 -Action Allow

New-NetFirewallRule -DisplayName "EDSC HTTP" `
  -Direction Inbound -Protocol TCP -LocalPort 5000 -Action Allow
```

## Common Issues

### Build Issue: Missing .NET SDK

**Error**: `The current .NET SDK does not support 'net8.0' as a target`

**Solution**:
```bash
# Install .NET 8.0 SDK
winget install Microsoft.DotNet.SDK.8

# Or download from: https://dotnet.microsoft.com/download/dotnet/8.0
```

### Build Issue: Android SDK Not Found

**Error**: `error XA5300: The Android SDK Directory could not be found`

**Solution**:
1. Install Android SDK via Visual Studio Installer
2. Or set environment variable:
   ```
   ANDROID_SDK_ROOT=C:\Users\YourName\AppData\Local\Android\Sdk
   ```

### Build Issue: NuGet Package Restore Failed

**Error**: `Unable to find package Avalonia.Desktop`

**Solution**:
```bash
# Clear NuGet cache
dotnet nuget locals all --clear

# Restore again
dotnet restore

# If still fails, check internet connection and NuGet sources
dotnet nuget list source
```

### Build Issue: InputSimulatorCore Not Found

**Error**: `The type or namespace name 'WindowsInput' could not be found`

**Solution**:
```bash
# Restore NuGet packages
cd src/EDSC.Desktop
dotnet restore

# Verify package is listed
dotnet list package
```

### Runtime Issue: DLL Not Found

**Error**: `System.IO.FileNotFoundException: Could not load file or assembly`

**Solution**:
Use self-contained publish:
```bash
dotnet publish -c Release -r win-x64 --self-contained true
```

### Runtime Issue: Port Already in Use

**Error**: `System.IO.IOException: Failed to bind to address http://[::]:5000`

**Solution**:
1. Change port in `config.json`
2. Or find and stop process using port 5000:
   ```powershell
   # Find process
   netstat -ano | findstr :5000

   # Stop process
   taskkill /PID <process-id> /F
   ```

## Verification

### Verify PC Build

```bash
# Run server
cd src/EDSC.Desktop/bin/Debug/net8.0-windows
.\EDSC.Desktop.exe

# Should output:
# info: Now listening on: http://[::]:5000
# info: Application started
```

**Test HTTP server**:
```bash
curl http://localhost:5000/
# Should return: {"service":"EDSC","status":"running","version":"1.0.0"}
```

### Verify Android Build

1. Install APK on device
2. Launch app
3. Should see "EDSC - Elite Dangerous" header
4. Connection view should load
5. Tap "Discover Servers" - should work without crash

## Build Performance Tips

### Faster Builds

```bash
# Skip building Android if not needed
dotnet build --project src/EDSC.Desktop/EDSC.Desktop.csproj

# Incremental build (only changed files)
dotnet build --no-restore

# Parallel build
dotnet build -m:4  # Use 4 CPU cores
```

### Reduce Build Output

```bash
# Quiet mode
dotnet build -v quiet

# Minimal output
dotnet build -v minimal
```

### Clean Builds

```bash
# Clean before building
dotnet clean
dotnet build

# Or
dotnet build --no-incremental
```

## Build Scripts

### Windows Batch Script

Create `build.bat`:
```batch
@echo off
echo Building EDSC...
dotnet restore
dotnet build -c Release
if %ERRORLEVEL% EQU 0 (
    echo Build succeeded!
    dotnet run --project src\EDSC.Desktop\EDSC.Desktop.csproj
) else (
    echo Build failed!
    exit /b 1
)
```

### PowerShell Script

Create `build.ps1`:
```powershell
Write-Host "Building EDSC..." -ForegroundColor Green

# Restore
dotnet restore
if ($LASTEXITCODE -ne 0) { exit 1 }

# Build
dotnet build -c Release
if ($LASTEXITCODE -ne 0) { exit 1 }

# Publish
dotnet publish src/EDSC.Desktop/EDSC.Desktop.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true

Write-Host "Build complete!" -ForegroundColor Green
Write-Host "Output: src/EDSC.Desktop/bin/Release/net8.0-windows/win-x64/publish/"
```

Run: `powershell -ExecutionPolicy Bypass -File build.ps1`

## Continuous Integration

### GitHub Actions Example

Create `.github/workflows/build.yml`:
```yaml
name: Build EDSC

on: [push, pull_request]

jobs:
  build:
    runs-on: windows-latest

    steps:
    - uses: actions/checkout@v3

    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: '8.0.x'

    - name: Restore dependencies
      run: dotnet restore

    - name: Build
      run: dotnet build --no-restore -c Release

    - name: Test
      run: dotnet test --no-build -c Release

    - name: Publish
      run: dotnet publish src/EDSC.Desktop/EDSC.Desktop.csproj
           -c Release -r win-x64 --self-contained true

    - name: Upload artifact
      uses: actions/upload-artifact@v3
      with:
        name: EDSC-Windows
        path: src/EDSC.Desktop/bin/Release/net8.0-windows/win-x64/publish/
```

## Additional Resources

- **AvaloniaUI Documentation**: https://docs.avaloniaui.net/
- **.NET Publishing Guide**: https://learn.microsoft.com/en-us/dotnet/core/deploying/
- **Android App Publishing**: https://developer.android.com/studio/publish

## Support

For build issues:
1. Check this document first
2. Search existing GitHub issues
3. Create new issue with build output
4. Include: OS, .NET version, error messages

---

**Last Updated**: December 2025

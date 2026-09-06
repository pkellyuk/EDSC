# EDSC - Elite Dangerous and Star Citizen Ship Controls

Remote control your ship in Elite Dangerous or Star Citizen from your phone's browser, and track your head with the phone's camera.

[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-blue)](https://dotnet.microsoft.com/download)
[![AvaloniaUI](https://img.shields.io/badge/AvaloniaUI-11.0-purple)](https://avaloniaui.net/)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

## What is EDSC?

EDSC is a Windows desktop app that serves a web control panel for Elite Dangerous and Star Citizen. Scan a QR code from the desktop UI and control ship functions from any phone browser on the same network. The same page can turn the phone into a head tracker. Each game has its own button layout; a switcher in the Buttons tab picks which one the phone shows.

**How it works:**
- Desktop app runs a local HTTP/HTTPS server
- UI shows a QR code to open the web page on the phone
- Phone browser sends button presses, which the desktop app turns into keypresses in the game
- Optionally the phone runs face tracking and sends head pose to the PC, which feeds it to the game

## Features

- QR code web UI (no mobile app required)
- HTTP (port 9000) and HTTPS (port 9001) with a self-signed certificate the app can install for you
- Voice control with continuous listening
- Keyboard simulation, including modifier combos such as `LSHIFT+U`
- Head tracking two ways: on the phone with MediaPipe, or on the PC with ONNX models
- Head pose output to Opentrack (UDP) or straight to the game's TrackIR interface with no Opentrack running
- Drag-and-drop button editor on the desktop, with live updates to the phone
- Separate layouts for Elite Dangerous and Star Citizen, switched from the desktop; the phone page rebrands itself to match
- Import of your Elite Dangerous or Star Citizen key bindings to build and group the buttons automatically
- Long-press buttons for games that put a second action on a held key (hold to exit seat, hold for NAV mode)

## Quick Start

Not building from source? Grab `EDSCv2-Setup.exe` from the [Releases page](https://github.com/pkellyuk/EDSC/releases) and skip to step 2.

### 1) Build and run the desktop app

```bash
dotnet run --project src/EDSC.Desktop
```

The window opens with two tabs: **Tracking** (QR code, sensitivity, output, preview) and **Buttons** (layout editor).

### 2) Open the web UI on the phone

1. Select the correct IP address in the Tracking tab
2. Scan the QR code with your phone
3. Accept the certificate warning, or click **Install SSL Certificate** on the PC first to avoid it

The phone page follows the PC: when you save a layout or restart the app, the page updates itself.

The page is laid out as a cockpit display: category keys around the edges, the selected category's buttons in the centre, and the tracking preview docked bottom-right (tap it to re-centre). **Fullscreen** hides the browser chrome, and **Settings** opens the tracking options: track on phone, camera size, mesh detail, and reload.

## Buttons

### Editing the layout

Open the **Buttons** tab on the desktop.

- The **Game** selector chooses which layout you are editing and which one the phone shows. Switching is saved immediately and the phone follows within a few seconds; the layout for the other game is kept untouched
- Drag a button onto a category to move it there, or onto another button to insert before it
- Click a button to edit its label, key, hold time, icon, colour and id
- **Hold for** keeps the key down for that many milliseconds. Leave it at 0 for a normal tap; Star Citizen's long-press actions (NAV mode on B, exit seat on Y, autoland on N) want around 700
- **Add category** creates an empty group to drag into; empty groups can be removed with the X
- **Save** writes both layouts to `config.json`; the phone reloads its layout within a few seconds

Keys use the names of Windows virtual keys: letters and digits (`U`, `4`), `F1`..`F24`, `NUMPAD5`, `HOME`, `DELETE`, `RETURN`, `BACK`, `ESCAPE`, `OEM_PLUS`, and so on. Prefix modifiers with `+`, for example `LSHIFT+U` or `LCONTROL+LMENU+SPACE`.

### Import from Elite Dangerous

**Import from Elite Dangerous** reads the game's own bindings and rebuilds the button list grouped by function.

- It reads which presets are active from `%LOCALAPPDATA%\Frontier Developments\Elite Dangerous\Options\Bindings\StartPreset*.start`
- Custom presets come from that folder; stock presets come from the game install's `ControlSchemes` folder, found via Steam or the usual launcher paths
- Only keyboard bindings can be used. Actions bound to a pad or HOTAS only are shown greyed as **not bound**; bind a key in the game's Controls and import again
- Existing buttons keep their colour and icon; buttons the importer does not know about are kept

If the game install is not found, set `eliteControlSchemesPath` in `config.json` to the `ControlSchemes` folder.

### Import from Star Citizen

Select **Star Citizen** in the Game selector. The first time, the layout is built automatically; **Import from Star Citizen** rebuilds it later.

- Star Citizen only writes the actions you have rebound to `<install>\StarCitizen\LIVE\user\client\0\Profiles\default\actionmaps.xml`. Everything else uses the game's stock key, which EDSC carries for the actions it knows about (Alpha 4.x defaults)
- The install is found via the RSI Launcher's library folder, `C:\Program Files\Roberts Space Industries`, or the root of any drive. LIVE is preferred over PTU. If it is not found the stock keys are still used; set `starCitizenPath` in `config.json` to the channel folder (the one containing `Bin64`) if yours lives elsewhere
- Keyboard rebinds replace the default; a joystick-only rebind leaves the keyboard key in place. Actions rebound to a double tap are shown as **not bound**
- Long-press actions come in with a hold time already set. Check the stock keys against your own game version, since CIG moves defaults between patches
- The game must be set to TrackIR under Options > Head Tracking for head tracking; the **Head Tracking** button (Numpad / by default) toggles it in game

## Head Tracking

Press **Face Tracking** on the phone page. Two modes are available via the **Track on phone** switch on the page.

**On the phone (default).** The browser runs Google's MediaPipe Face Landmarker on the camera, draws the face mesh, and sends only the pose to the PC. This is the more accurate and lower-latency option and needs no models on the PC. The phone needs internet on first use to fetch the model.

**On the PC.** The phone streams video and the PC runs the AITrack ONNX models in `src/EDSC.Desktop/Models`. Useful if the phone is too slow for MediaPipe.

### Output

In the Tracking tab, **Pose Output** chooses where the pose goes.

- **Opentrack (default):** UDP to `127.0.0.1:4242`. Use Opentrack's "UDP over network" input and its own centring, filters and axis mapping.
- **Send directly to game:** writes the pose into the FreeTrack shared memory that Opentrack's NPClient DLLs read, so the game receives it with no Opentrack running. Opentrack must have been installed and run once with the "freetrack 2.0 Enhanced" output so its DLLs are registered. Do not run Opentrack at the same time.

In direct mode the first pose sets the centre. Press **Center view** or the **=** key at any time to re-centre; the hotkey works while the game has focus and ignores keypresses EDSC itself simulates.

### Sensitivity

The sliders scale movement after centring. Rotation at 1x is real degrees. Position at 1x is real centimetres, which is a small fraction of the TrackIR axis range, so try around 3x and adjust. **Smoothing** drives both the tracker's smoothing and, in direct mode, an adaptive filter that removes jitter at rest without adding lag in motion.

**Eye nudge** adds a fraction of where your eyes are looking to the head direction, so a glance to the side leads the view a little without turning your head. Phone-side tracking measures gaze from the iris landmarks it already computes, so it costs nothing on the phone and adds two numbers to each pose message. A small dead zone means a resting glance does nothing. Blinks are detected early from the lid gap against each eye's normal opening and hold the last value, and the gaze gets its own gentle smoothing so it leads the view rather than jerking it. Head pitch is also held through each blink, because the face model tilts slightly as the lids move. 0% turns it off. The preview panel shows the irises, a ray from each eye in the direction of gaze, and an inset view window: green is the head, yellow is where the eyes look, and the white ring is what the game receives.

**Show face mesh preview** draws the tracked face outline in the desktop panel. Only the mesh lines are sent from the phone (about 1 KB a frame); the camera image never leaves it. Turn the preview off to save a little CPU on both sides.

Settings persist in `config.json` under `tracking`:

```json
{
  "tracking": {
    "translationScale": 3.0,
    "yawScale": 1.0,
    "pitchScale": 1.0,
    "rollScale": 1.0,
    "smoothingStrength": 0.5,
    "gazeNudge": 0.2,
    "directOutput": true,
    "showPreview": true,
    "previewMode": "LandmarksOnly"
  }
}
```

## Voice Commands

Click **Voice** on the phone page to enable continuous listening, then say a button label such as "landing gear" or "hardpoints". Matching is fuzzy and a short cooldown prevents double-firing. Voice needs HTTPS and, on most browsers, an internet connection for speech recognition.

## HTTPS and Certificate

The web UI is served on HTTP (9000) and HTTPS (9001); plain HTTP requests from other devices are redirected to HTTPS. The camera and microphone APIs require HTTPS.

The app generates a self-signed certificate. Click **Install SSL Certificate** to add it to the Windows trusted store (needs a UAC prompt) so browsers on this PC trust it. Phones will still show a one-time warning; accept it with "Advanced" then "Proceed".

## Configuration

`config.json` lives in `%APPDATA%\EDSC`. On first run it is created with a default layout, or migrated from a copy next to the executable left by an older version. `buttons` holds the Elite Dangerous layout (the name is kept so older files load unchanged), `starCitizenButtons` the Star Citizen one, and `activeGame` (`elite` or `starcitizen`) says which the phone shows. Each button looks like:

```json
{
  "id": "hardpoints",
  "key": "U",
  "label": "Hardpoints",
  "category": "Combat",
  "iconSvg": "hardpoints.svg",
  "color": "#6B7280",
  "size": 80,
  "holdMs": 0
}
```

SVG icons are served from `src/EDSC.Desktop/Assets/Icons`. The `server.port` setting changes the HTTP port; HTTPS uses the next port up.

## API Reference

| Method | Path | Purpose |
|---|---|---|
| GET | `/` | Health check |
| GET | `/web` | Phone page |
| GET | `/config` | Button layout |
| GET | `/config/version` | Layout version and app stamp, polled by the phone |
| POST | `/command` | `{ "buttonId": "hardpoints", "key": "U" }` presses a key |
| WS | `/video` | JPEG frames from the phone for PC-side tracking |
| WS | `/pose` | Poses (JSON) and face mesh frames (binary line segments) from phone-side tracking |

Pose messages are `{"t":"pose","yaw":..,"pitch":..,"roll":..,"x":..,"y":..,"z":..,"gy":..,"gp":..}` in degrees and centimetres, or `{"t":"lost"}`. `gy` and `gp` are eye gaze yaw and pitch relative to the head and are omitted while the eyes are closed.

## Project Structure

```
EDSC/
+- EDSC.sln
+- config.json                       # Default button layout
+- src/
|  +- EDSC/                          # Shared models, view models and views
|  +- EDSC.Desktop/                  # Desktop app, server, tracking, output
|     +- Services/HttpCommandServer.cs            # Server and the embedded phone page
|     +- Services/FaceTrackingService.cs          # PC-side ONNX tracking
|     +- Services/PnpSolver.cs                    # Pose from landmarks
|     +- Services/PoseOutputRouter.cs             # Centring, gain, filtering, output choice
|     +- Services/FreeTrackSharedMemorySender.cs  # Direct-to-game output
|     +- Services/EliteBindingsService.cs         # Elite bindings import
|     +- Views/ButtonEditorView.axaml             # Drag-and-drop layout editor
+- installer/EDSC.iss                # Inno Setup script
```

## Troubleshooting

**Web UI does not load**
- Ensure phone and PC are on the same network and the right IP is selected
- Allow TCP ports 9000 and 9001 in Windows Firewall

**Tracking works but the game does not move**
- In direct mode, check the Pose Output status says the game is connected. Elite reports as id 3475
- Make sure Opentrack is not also running in direct mode
- Press **=** to re-centre after sitting down

**Position barely moves**
- Raise the Position slider; a few centimetres of lean is a small fraction of the TrackIR range

**A button does nothing in game**
- The key must match a keyboard binding in the game's active preset. Use **Import from Elite Dangerous** to see which actions are bound

**Phone tracking is slow or jumpy**
- The readout on the phone shows inference time, the whole-frame time, whether the GPU is in use, and what the camera agreed to (`cam 640x480@30fps`). If the camera reports 15 fps the room is too dark; more light helps more than any setting
- The **Camera** and **Mesh** selectors in the phone page's **Settings** drawer trade detail for speed. Outline mesh is the default; 480x360 usually gets a mid-range phone from under 20 to around 30 poses per second
- Chrome tends to do better than other Android browsers
- In direct output mode the PC resamples the pose stream to 120 Hz, so even 18 poses per second should not stair-step in the game. The Pose Output status shows the measured input rate

## Installer (EXE)

```powershell
dotnet publish src/EDSC.Desktop/EDSC.Desktop.csproj -c Release -r win-x64 --self-contained true
iscc installer/EDSC.iss
```

The output is `installer/EDSCv2-Setup.exe`. Inno Setup 6 is needed (`winget install JRSoftware.InnoSetup`). Releases are published on the GitHub Releases page.

## Installing a release

Download `EDSCv2-Setup.exe` from the latest GitHub release and run it. Windows SmartScreen may warn because the installer is not code-signed; choose "More info" then "Run anyway". The installer offers a per-user or all-users install and creates a desktop shortcut.

## Security Considerations

- No authentication; local network only
- The certificate is self-signed and only trusted where you install it

## License

MIT License - See LICENSE file for details

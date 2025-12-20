# Repository Guidelines

## Project Structure
- `EDSC.sln` is the solution entry point.
- `src/EDSC/` is the shared Avalonia library (Models, Services, ViewModels, Views).
- `src/EDSC.Desktop/` is the Windows headless server (HTTP + keyboard simulation).
- `src/EDSC.Android/` is the Android client.
- `config.example.json` shows the expected runtime configuration.

## Build, Test, and Development Commands
- `dotnet restore` restores NuGet packages for the solution.
- `dotnet build` builds all projects (Android requires the SDK).
- `dotnet build src/EDSC.Desktop/EDSC.Desktop.csproj` builds the PC server only.
- `dotnet run --project src/EDSC.Desktop/EDSC.Desktop.csproj` runs the server locally.
- `dotnet build src/EDSC.Android/EDSC.Android.csproj` builds the Android app.

## Coding Style & Naming Conventions
- Use Allman braces and keep methods small with early returns.
- Prefer `Debug.WriteLine` logging at entry/exit and key steps, consistent with existing services.
- C# naming: PascalCase for types/methods/properties, camelCase for locals/parameters, `I` prefix for interfaces.
- XAML views live under `src/EDSC/Views/` with matching `*ViewModel` classes in `src/EDSC/ViewModels/`.

## Testing Guidelines
- There are no automated tests in this repository yet; rely on manual checks.
- Health check: `curl http://localhost:5000/` should return service status JSON.
- Command check: `curl -X POST http://localhost:5000/command -H "Content-Type: application/json" -d '{"buttonId":"test","key":"F1","timestamp":123}'`.
- Discovery check: use UDP broadcast (see `README.md` or `DISCOVERY_INTEGRATION.md`).

## Commit & Pull Request Guidelines
- No Git history is available in this workspace, so commit message conventions are unknown.
- Use clear, scoped commit messages (e.g., `desktop: add config validation`).
- PRs should describe the change, include test steps, and mention any config or firewall changes.

## Security & Configuration Tips
- The server runs unauthenticated on the local network (ports 5000/5001). Use trusted networks only.
- Place `config.json` next to the desktop executable (`src/EDSC.Desktop/bin/Debug/net8.0-windows/` in Debug builds).
- If discovery fails, confirm firewall rules for UDP 5001 and TCP 5000.

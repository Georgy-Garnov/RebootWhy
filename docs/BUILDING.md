# Building RebootWhy from source

## Requirements

* **Windows 10 or 11, x64.** The app uses WPF, the Windows event log and Windows Update APIs. The Core library and most tests build on any OS, but the app and the Update Orchestrator log tests need Windows.
* **.NET 10 SDK** ([download](https://dotnet.microsoft.com/download/dotnet/10.0)). Check with `dotnet --version`.
* Optional: Visual Studio 2026, JetBrains Rider or VS Code with the C# Dev Kit.

No other tools or paid components are needed. NuGet packages (NAudio, NAudio.Vorbis, System.ServiceProcess.ServiceController, System.Diagnostics.EventLog, xUnit) are restored automatically.

## Get the code

```powershell
git clone https://github.com/Georgy-Garnov/RebootWhy.git
cd RebootWhy
```

## Build, test, run

```powershell
dotnet build
dotnet test
dotnet run --project src/RebootWhy.App
```

Useful variants:

```powershell
# Tray-only mode, as at Windows sign-in
dotnet run --project src/RebootWhy.App -- --tray

# Console analyzer: restart history for 90 days with the evidence
dotnet run --project src/RebootWhy.Cli -- --days 90 --evidence

# Analyze a log exported from another computer (wevtutil epl System C:\logs\System.evtx)
dotnet run --project src/RebootWhy.Cli -- --file C:\logs\System.evtx

# Windows Update forecast in the console, in Russian
dotnet run --project src/RebootWhy.Cli -- --updates --lang ru
```

## Publish a single exe

```powershell
dotnet publish src/RebootWhy.App -p:PublishProfile=win-x64
```

The result is `publish\win-x64\RebootWhy.exe`, a self-contained single file of about 70 MB with the .NET runtime and all translations built in. It runs on a clean Windows 10/11 x64 without installing anything. The profile is in `src/RebootWhy.App/Properties/PublishProfiles/win-x64.pubxml`.

### If the service is installed

The service runs from the folder it was installed from and keeps `RebootWhy.exe` open, so publishing over it fails with "file in use". Before publishing:

```powershell
sc.exe stop RebootWhy          # stop the service
# close RebootWhy in the tray (right-click → Exit)
dotnet publish src/RebootWhy.App -p:PublishProfile=win-x64
sc.exe start RebootWhy
```

Or reinstall the service from the new exe (*Settings → Startup and service*).

## Building from WSL

The Windows SDK can be called from WSL directly:

```bash
"/mnt/c/Program Files/dotnet/dotnet.exe" build
"/mnt/c/Program Files/dotnet/dotnet.exe" test
```

Keep the repository on the Windows file system (e.g. `/mnt/c/...`) so that Windows tools see the same paths.

## Icons

The icons in `src/RebootWhy.App/Assets` are drawn by a script, so they can be changed without a graphics editor:

```powershell
powershell -ExecutionPolicy Bypass -File tools/make-icons.ps1            # regenerate .ico files
powershell -ExecutionPolicy Bypass -File tools/make-icons.ps1 -Preview   # also PNG previews in tools/preview
```

## Continuous integration

`.github/workflows/build.yml` builds, tests and publishes on every push and pull request (Windows runner). The exe is attached as a workflow artifact. Pushing a tag `v*` (e.g. `v1.0.0`) runs `.github/workflows/release.yml`, which creates a GitHub release with `RebootWhy.exe` and its SHA-256 checksum.

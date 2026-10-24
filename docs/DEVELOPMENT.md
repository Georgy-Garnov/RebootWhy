# Development guide

See [BUILDING.md](BUILDING.md) for setting up and building. This page covers how the code is organized and how to work on it.

## Solution layout

| Project | Target | What's inside |
|---|---|---|
| `src/RebootWhy.Core` | `net10.0` | All analysis, no UI: event log reading, restart classification, Windows Update forecast, monitoring engine and the service ↔ app pipe protocol, localized texts |
| `src/RebootWhy.App` | `net10.0-windows` (WPF + WinForms `NotifyIcon`) | The window, tray icon, alerts, settings, Windows integration and the service host. Builds `RebootWhy.exe` |
| `src/RebootWhy.Cli` | `net10.0-windows` | `rebootwhy-cli`, a console front end for debugging the analyzer and examining `.evtx` files |
| `tests/RebootWhy.Core.Tests` | `net10.0` | xUnit tests for Core |
| `tools/make-icons.ps1` | PowerShell | Draws the app and tray icons |

### Core (`src/RebootWhy.Core`)

| Folder | Responsibility | Key types |
|---|---|---|
| *(root)* | Model and constants | `SystemEvent`, `RebootRecord`, `Providers`, `EventCatalog`, `Enums` |
| `Sources` | Reading events | `WindowsEventLogSource` (live log or `.evtx`), `EventQuery`, `EventXmlParser`, `WindowsSystemInfo` |
| `Analysis` | Restart history | `RebootHistory` → `RebootAnalyzer` → `BootClusterer` + `RebootClassifier`; `ShutdownRequest`, `ShutdownReason`, `BugcheckCatalog`, `ProcessClassifier`, `UptimeCalculator` |
| `Updates` | Windows Update | `UpdateRebootMonitor` (full cycle) → `UsoLogSource` + `UsoXmlParser` + `WindowsUpdateEnvironmentReader` → `UpdateRebootAnalyzer` → `RebootForecast`; `WindowsUpdateCatalogReader` (history and known updates) |
| `Monitoring` | Watching and IPC | `ForecastEngine` (periodic and on-change recalculation), `ForecastServer` / `ForecastClient` (named pipe), `ForecastProtocol`, `RebootSimulation` |
| `Text` | Everything the user reads | `Display` (formats), `RecordFacts`, `RecordText`, `EventDescriber`, `UpdateText`, `ForecastText` |
| `Properties` | Resources | `Strings.resx` + `Strings.ru/uk/hy.resx` |

Rules that keep Core testable:

* **Analysis returns data, `Text` renders it.** A forecast carries `ForecastNote` / `ForecastProblem` values instead of sentences, because the service and the app may use different languages. Restart records are rendered when they're built, since they're always built in the app's process.
* **Never parse event message texts.** Use `EventData` fields and codes, so the analysis works on any Windows language.
* **Undocumented formats fail loudly.** If the USO log can't be read, the forecast lists a problem; it doesn't report "no restart expected".

### App (`src/RebootWhy.App`)

| Folder | Responsibility |
|---|---|
| `Program.cs` | One exe, three modes: window, `--tray`, `--service`. Single instance via a mutex; a second launch asks the first one to show its window. Sets the UI language before anything is shown |
| `AppHost.cs` | Wires everything together: settings, forecast source (service or in-process engine), tray icon, alerts, windows |
| `Monitoring` | `ForecastCoordinator` (service connection with fallback to the local engine), `AlertCoordinator` (when to notify, show the window, play the sound, snooze) |
| `ViewModels`, `Views`, `MainWindow.xaml` | MVVM UI. `MainViewModel` (Restarts tab), `UpdatesViewModel` (Updates tab), `ForecastViewModel` (alert window), `SettingsViewModel` |
| `Platform` | Windows integration: `ServiceSetup` (install/start/stop the service), `UpdatePolicies`, `Autostart`, `Elevation` (UAC), `AlertSound` (NAudio), `UserPresenceKeeper` (power request), `WindowPlacement` (multi-monitor aware) |
| `Settings` | `AppSettings` + `SettingsStore` (`%AppData%\RebootWhy\settings.json`), `ViewState` (column widths, period, window placement), `UpdatesCache` (`%LocalAppData%\RebootWhy\updates-cache.json`) |
| `Themes/Theme.xaml` | All styles: palette, buttons, toggle switches, list views, tabs, search box |
| `ServiceHost` | `RebootWhyService` (`ServiceBase`) hosting `ForecastEngine` + `ForecastServer` |

## Localization

The UI is available in **English** (neutral resources), **Russian**, **Ukrainian** and **Armenian**. The user picks the language in *Settings*. The default follows the Windows display language and falls back to English.

* Strings live in `.resx` files: `src/RebootWhy.Core/Properties/Strings*.resx` (analysis texts) and `src/RebootWhy.App/Properties/Strings*.resx` (UI). The SDK generates the strongly typed `Strings` classes at build time.
* XAML uses `{x:Static l:Strings.Key}` with `xmlns:l="clr-namespace:RebootWhy.App.Properties"`. Code uses `Strings.Key` and `Display.Format(Strings.Key, args)`.
* Date and time patterns are resources too (`Format_*`) and are applied with the invariant culture, so a user's custom regional format doesn't leak into the UI.
* Code and comments are in English. **Non-English text belongs only in `Strings.<lang>.resx`.**
* `LocalizationTests` checks that every key exists and isn't empty in every language and that the `{0}` placeholders match.

### Adding a language

1. Copy `Strings.resx` to `Strings.<code>.resx` in both `Properties` folders and translate the values (Visual Studio, Rider's *Localization Manager* or any resx editor).
2. Add the code to `Localization.Supported` (`src/RebootWhy.App/Infrastructure/Localization.cs`) and to `LocalizationTests.Languages`.
3. Run `dotnet test`, then check the UI. Long words may need wider columns or buttons.

## Tests

```powershell
dotnet test
```

* `RebootAnalyzerTests`, `RecordTextTests`, `DecoderTests`, `UptimeCalculatorTests`: synthetic System logs built with `EventLogBuilder`.
* `UpdateRebootAnalyzerFixtureTests`: **real** Update Orchestrator logs from an unwanted night-time restart (`Fixtures/uso-2026-10-24`). They need `tracerpt.exe`, so they run on Windows only.
* `ForecastChannelTests`: the service ↔ app pipe end to end, including request/reply correlation and the reaction test.
* `RebootSimulationTests`, `UpdateTextTests`, `EventXmlParserTests`, `LocalizationTests`.

Tests run with the `en-US` culture (`TestCulture.cs`), so expectations are English on any machine.

## Debugging tips

* **Reaction test:** tray menu (*Test the restart reaction*) or *Settings → Alerts → Start test*. A synthetic driver update "waits for a restart" for 5 minutes with a scheduled restart in 3 minutes. It goes through the real analyzer, alerts and sound. Nothing in Windows changes.
* **Sample alert:** *Settings → Alerts → Show sample* opens the alert window with sample data. It's marked as a sample.
* **Console:** `rebootwhy-cli --updates` prints the forecast with notes and problems. `--evidence` prints the events behind every restart.
* **Event logs from users:** ask for `wevtutil epl System System.evtx` and open the file with *Open .evtx log…* or `rebootwhy-cli --file`.
* **USO logs:** `tracerpt <file>.etl -of XML -o out.xml` decodes them the same way the app does.
* **Service:** it runs from the folder where it was installed. Stop it with `sc.exe stop RebootWhy` before rebuilding. Use *Settings → Startup and service* to reinstall it from a new build.

## Conventions

* Follow `.editorconfig`. Keep the existing style: file-scoped namespaces, `sealed` classes, records for data, nullable enabled.
* Comment *why*, not *what*. Comments are in English.
* No hard-coded user-visible strings: add a resource key in all languages.
* Keep Core free of UI dependencies. Windows-only APIs are marked with `[SupportedOSPlatform("windows")]`.
* A change in analysis logic needs a test, preferably one built from a real log.

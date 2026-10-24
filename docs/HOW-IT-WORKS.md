# How RebootWhy works

RebootWhy only **reads** data that Windows already keeps. There are two kinds of analysis:

* **Restart history**, from the *System* event log;
* **Windows Update forecast**, from the Update Orchestrator logs and the Windows Update state.

All analysis code is in `src/RebootWhy.Core` and doesn't depend on the UI.

## Restart history

Source: the live *System* log (`EventLogReader`) or an exported `.evtx` file. The XPath query filters only by providers and time. Event IDs are selected in code, because Windows event log XPath limits the number of conditions in a query.

### 1. Finding boots

Boot-start markers are grouped into individual boots (`BootClusterer`):

| Event | Meaning |
|---|---|
| Kernel-General 12 | The OS started (kernel start time) |
| Kernel-Boot 27 | Boot type: full boot, Fast Startup, resume from hibernation |
| EventLog 6005 | The event log service started (only on a full boot or Fast Startup) |

A repeated marker always starts a new boot, so quick boot loops aren't merged.

### 2. Crash or clean shutdown?

Crash markers are searched in the first 20 minutes after the boot:

| Event | What it tells |
|---|---|
| Kernel-Power 41 | The previous session ended without a clean shutdown. Its fields give the bugcheck code, power-button presses and sleep state |
| WER 1001 (BugCheck) | Bugcheck code and memory dump path |
| EventLog 6008 | "The previous system shutdown was unexpected" |
| WHEA-Logger | Hardware errors (CPU, memory, PCIe) |

Bugcheck codes are mapped to names and short hints (`BugcheckCatalog`). The code `0x1C8` (MANUALLY_INITIATED_POWER_BUTTON_HOLD) means the power button was held down.

### 3. Who asked for the restart

For a clean shutdown, RebootWhy takes the last **User32 1074** event of the previous session:

* the **process** (`MoUsoCoreWorker.exe` → Windows Update, `Explorer.EXE` → the user, `shutdown.exe` → a command, `msiexec.exe` → an installer…);
* the **user** the request was made for (`NT AUTHORITY\SYSTEM` or a real person);
* the **reason code** (`SHTDN_REASON_*`: planned or not, major and minor reason, e.g. *operating system — service pack*);
* the shutdown **type** and the **comment**.

Windows writes the shutdown type in its display language. RebootWhy recognizes the English values. For other languages it infers *restart* or *shutdown* from how long the computer stayed off.

A clean shutdown without 1074 (EventLog 6006, Kernel-General 13, Kernel-Power 109) is reported as *"clean shutdown, initiator not recorded"*. That's typically a short press of the power button or a critically low battery.

### 4. Extras

* updates installed before the restart (WindowsUpdateClient 19), with Defender definitions and Store apps filtered out;
* a cleared log (Eventlog 104);
* Fast Startup: a *"Shut down"* that didn't restart the kernel;
* uptime is calculated from the kernel counter (`Environment.TickCount64`) and the active time without sleep (`QueryUnbiasedInterruptTime`).

Message texts are never parsed, only `EventData` fields and codes. That's why the analysis works on any Windows language.

## Windows Update forecast

### Sources

| Source | What it gives | Rights |
|---|---|---|
| `C:\ProgramData\USOShared\Logs\System\UpdateSessionOrchestration.*.etl` | Update Orchestrator (USO) decisions, the update queue, task scheduling | user |
| Windows Update Agent (`Microsoft.Update.SystemInfo`) | `RebootRequired` | user |
| `HKLM\…\Component Based Servicing\RebootPending` | Pending servicing | user |
| Active hours (`HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings`, policies) | When Windows won't restart | user |
| Task Scheduler `\Microsoft\Windows\UpdateOrchestrator` | The exact next run of `Schedule Work`, `Schedule Wake To Work`, `Reboot_AC`… | admin (service) |

The ETL files are decoded with the built-in `tracerpt.exe` to XML and parsed by `UsoXmlParser`. Only changed files are decoded again. USO writes local time with a broken offset, which is taken into account.

### Analysis (`UpdateRebootAnalyzer`)

1. **What waits for a restart.** In the last queue snapshot `id[Action],…`, an update in the `Commit` state is installed and will be committed on restart. USO's `ManagerDiagnostics` summary (`Installed-PendingReboot`) is used as well. Events before the last boot are ignored.
2. **When.** No earlier than the end of active hours, and at the next run of a *work* task (`Schedule Work`, `Schedule Wake To Work`, `Schedule Maintenance Work`, `Reboot_AC`, `Reboot_Battery`). `Schedule Scan` only searches for updates and is ignored. Within an hour → *imminent*; later → *planned*.
3. **Why without asking.**
   * a `Start action` decision with tag `UAP` → *the update is treated as approved by you earlier*, so there will be no separate question;
   * `isOverdue=true` → Windows will push harder;
   * `Current Active Deferrals` with `UserEngaged` → the restart was postponed because you were using the computer;
   * the `NoAutoRebootWithLoggedOnUsers` policy.

The forecast is language-neutral data (`RebootForecast`, `ForecastNote`, `ForecastProblem`). The service and the app may run in different languages, so the app turns it into text (`ForecastText`).

If something can't be read, the forecast lists it under *problems* instead of pretending that all is clear.

### The real case behind the tests

`tests/RebootWhy.Core.Tests/Fixtures/uso-2026-10-24` holds the Update Orchestrator logs of an actual unwanted restart:

* 14:52, the user agreed to install a batch of updates; the Intel display driver failed (`0x80246007`) and was downloaded again;
* 15:11, the driver was staged with *"Update approved (UAP)"* → `Installed-PendingReboot`;
* in the evening, USO postponed the restart several times because the user was active;
* ~00:55 the user went idle; active hours ended at 01:00; `Schedule Work` at 01:15 → `Schedule Wake To Work` at 01:30 → restart.

The tests check that RebootWhy would have reported *"Windows may restart the computer within the hour (around 01:15) to install Intel… Display Driver"* at 00:30.

## Warnings and the "keep in use" option

When the forecast changes, `AlertCoordinator` shows a notification and/or an always-on-top window: when a pending restart is first detected and again N minutes before the earliest restart time. Each alert plays the chosen sound, and the user can mute it for that alert only.

*Keep the computer in use* holds a power request (`PowerCreateRequest` with *display required* and *system required*, the same mechanism video players use) while a restart is pending, so Windows considers the user present. It doesn't block manual restarts.

## The service and the named pipe

`RebootWhy.exe --service` runs as `LocalSystem` (service name `RebootWhy`). It recalculates the forecast every 5 minutes and right after USO writes to its logs (`FileSystemWatcher` with debounce).

The app connects to the named pipe `RebootWhy.Forecast`. Each message is one JSON line:

| From the app | Reply |
|---|---|
| `subscribe` | the current forecast, then every recalculation |
| `analyze` (with id) | a fresh forecast with `replyTo` = id |
| `simulate` / `stop-simulation` | starts or stops the reaction test |

Pipe ACL: the service account, SYSTEM and Administrators have full control, authenticated local users can read and write, network access is denied. The service only reads data and recalculates. It accepts no commands that change the system.

Without the service, the app runs the same `ForecastEngine` in its own process and takes the schedule from the USO log.

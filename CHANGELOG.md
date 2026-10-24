# Changelog

All notable changes to this project are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses [Semantic Versioning](https://semver.org/).

## [1.0.0] - 2026-10-24

First public release.

### Restart history
- Uptime, last full boot and active time without sleep; Fast Startup and hibernation aware.
- A cause for every boot: Windows Update (automatic or by the user), user, command, installer, application, blue screen (bugcheck name, hint, dump path), power loss or hang, power button held, sleep failure, hibernation. Includes the confidence level and the log events used as evidence.
- WHEA hardware errors, installed updates, cleared-log and Fast Startup notes.
- Exported `.evtx` logs from other computers can be opened.
- Copy the details of any restart to the clipboard.

### Windows Update
- Forecast of Windows Update restarts from the Update Orchestrator logs: what is pending, the earliest restart time, and why Windows may restart without asking.
- Alerts: Windows notification, always-on-top window (on detection and N minutes before), custom sound (mp3/wav/ogg), snooze.
- Optional "keep the computer in use" while a restart is pending.
- Reaction test that simulates a pending restart without touching Windows.
- Updates tab: installation history and known but not installed updates, restart requirement highlighting, search and filters.
- Windows Update policy switches: no auto-restart with logged-on users, exclude drivers, Automatic Updates mode.

### App
- Optional Windows service with exact Task Scheduler data and push updates over a local named pipe.
- Tray icon that turns red when a restart is pending.
- English, Russian, Ukrainian and Armenian UI.
- Single self-contained exe; window size, position, columns and filters are remembered.

[1.0.0]: ../../releases/tag/v1.0.0

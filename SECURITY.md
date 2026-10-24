# Security policy

## What RebootWhy can do on your computer

* The app runs as the current user and **reads** the System event log, the Update Orchestrator logs, the Windows Update history and a few registry keys.
* With your consent (UAC), it can **change Windows Update policies** under `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate` and **install the optional service**.
* The service runs as `LocalSystem` and **only reads** the same data plus Task Scheduler. It exposes the named pipe `RebootWhy.Forecast`:
  * access: the service account, SYSTEM and Administrators have full control; authenticated local users can read and write; **network access is denied**;
  * commands: `subscribe`, `analyze`, `simulate`, `stop-simulation`. None of them change the system. Unknown or malformed messages are ignored.
* RebootWhy makes **no network connections** and collects no telemetry.

## Supported versions

Security fixes go into the latest release.

## Reporting a vulnerability

Please **do not open a public issue** for security problems. Use GitHub's [private vulnerability reporting](../../security/advisories/new) for this repository. Include the version, the steps to reproduce and the impact. You'll get an answer within a week. Once a fix is released, the issue will be credited to you unless you prefer otherwise.

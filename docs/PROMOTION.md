# Promotion kit

Ready-to-use texts for publishing RebootWhy. Not linked from the README; this page is for the maintainer.

## GitHub repository settings

**About (description):**
> Why did Windows restart? RebootWhy explains every restart from the event logs and warns you before Windows Update restarts your PC. Portable, private, open source.

**Website:** the latest release page.

**Topics:**
`windows` `windows-11` `windows-10` `windows-update` `reboot` `restart` `uptime` `event-log` `bsod` `wpf` `dotnet` `csharp` `system-tools` `troubleshooting` `sysadmin` `portable`

**Social preview** (*Settings → General → Social preview*, 1280×640): the main window screenshot (`docs/images/reboots.png`) cropped to the uptime cards and the list of causes, with the icon and the tagline *"Why did my PC restart?"*.

**Releases:** tag `v1.0.0` → the `Release` workflow attaches `RebootWhy.exe` and its SHA-256. Paste the [CHANGELOG](../CHANGELOG.md) section as the release notes and add the screenshots.

## Key messages

1. **It answers "why", not just "when".** Every restart gets a cause in plain words, with the log events as proof.
2. **It warns before Windows Update restarts your PC.** It reads the Update Orchestrator logs, the same data Windows uses to plan the restart.
3. **Private and portable.** One exe, no installer, no network, no telemetry.
4. **Honest.** It shows the evidence and the confidence level. If it can't check, it says so.

## Short pitch (≈ 280 characters)

> Windows restarted your PC overnight and lost your work? RebootWhy shows why every restart happened (Windows Update, BSOD, power loss, user…) with proof from the event logs, and warns you before Windows Update restarts again. Free, open source, one portable exe.

## Show HN / Reddit (r/Windows11, r/sysadmin, r/software)

**Title:** RebootWhy: find out why Windows restarted and get warned before Windows Update does it again

> My PC restarted at 01:30 one night to install an Intel display driver, with my work open. Windows Settings said nothing, and Event Viewer had thousands of entries.
>
> So I built RebootWhy, a small open-source Windows app (C#/.NET 10, WPF):
>
> - For every boot it names the cause: Windows Update (automatic or by the user), user, shutdown.exe, installer, blue screen (with the bugcheck name and dump path), power loss, power button held, sleep failure… and it shows the exact events behind the conclusion.
> - It reads the Update Orchestrator ETL logs (`C:\ProgramData\USOShared\Logs`) to see that an update is installed and waiting for a restart, when the next "Schedule Work / Wake To Work" task will run, and why Windows thinks it can restart without asking ("update treated as approved earlier"). Then it warns you with a notification, an always-on-top window and a sound of your choice.
> - It can keep the session "in use" while a restart is pending, and it toggles the relevant Windows Update policies.
>
> No network access and no telemetry. It's a single portable exe, MIT-licensed. The analysis is tested against the real logs of that night. Feedback on log formats from other Windows builds is very welcome.

## Habr / VC (Russian)

**Заголовок:** Почему перезагрузился Windows: разбираем журналы и предупреждаем о перезагрузке Windows Update заранее

> Однажды ночью Windows перезагрузил мой компьютер с открытой работой, чтобы поставить драйвер Intel. В параметрах Windows об этом ничего не было, а в «Просмотре событий» — тысячи записей. Я разобрался, как Windows на самом деле принимает решение о перезагрузке, и написал RebootWhy — бесплатную программу с открытым кодом.
>
> - Для каждой загрузки она называет причину: Windows Update, пользователь, команда, установщик, синий экран (с названием ошибки и дампом), пропадание питания — и показывает события журнала, на которых основан вывод.
> - Она читает журналы Update Orchestrator и заранее видит, что обновление ждёт перезагрузки, когда сработает задача Windows и почему Windows считает, что можно не спрашивать. Предупреждает уведомлением, окном поверх всех и звуком.
> - Один exe, без установки, без сети и телеметрии, лицензия MIT.
>
> В статье можно подробно разобрать, что пишет Update Orchestrator, как устроено «одобрено ранее (UAP)» и как с этим соотносится политика NoAutoRebootWithLoggedOnUsers.

## Where to post

* GitHub: topics, a release with screenshots, *Discussions* turned on for questions.
* Reddit: r/Windows11, r/Windows10, r/sysadmin, r/software, r/opensource (read each subreddit's self-promotion rules).
* Hacker News: *Show HN*.
* Habr (Russian), DOU (Ukrainian), Armenian IT communities, using the README in the matching language.
* Software catalogs: AlternativeTo, MajorGeeks, Softpedia, and later winget (`winget-pkgs` manifest) and Scoop. Package managers need a stable download URL from GitHub Releases.

## Before announcing

- [ ] The repository is public and the README images show up.
- [ ] Release `v1.0.0` has `RebootWhy.exe` and its checksum.
- [ ] Optional: code-sign the exe to avoid SmartScreen warnings (for example with a SignPath.io open-source certificate).
- [ ] Issues and Discussions are enabled; private vulnerability reporting is enabled (*Settings → Code security*).

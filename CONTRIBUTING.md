# Contributing to RebootWhy

Thanks for helping! Bug reports, log samples, translations and code are all welcome.

## Reporting a wrong or missing cause

Restart causes are reconstructed from logs, and Windows keeps changing what it writes. A report with data is the most valuable contribution:

1. In RebootWhy, select the restart and press **Copy** at the top of the details panel. Paste the text into the issue.
2. Say what really happened ("I pressed the power button", "the PC was off for the night"…).
3. If possible, attach the System log: `wevtutil epl System System.evtx`. **It contains your computer and user names.** Share it only if you're comfortable with that, or send it privately.

For a wrong Windows Update forecast, attach the output of `rebootwhy-cli --updates` and, if possible, the files from `C:\ProgramData\USOShared\Logs\System` for the relevant days.

## Translations

UI texts are in `src/RebootWhy.Core/Properties/Strings*.resx` and `src/RebootWhy.App/Properties/Strings*.resx`. To fix a translation, edit the value in the language file. To add a language, see [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md#adding-a-language). `dotnet test` checks that nothing is missing and that the `{0}` placeholders match.

## Code

1. Open an issue first for anything larger than a small fix, so we can agree on the approach.
2. Fork, create a branch, make the change, and add or update tests (`tests/RebootWhy.Core.Tests`).
3. `dotnet build` with no warnings and `dotnet test` green.
4. Open a pull request describing what changed and how you checked it. Screenshots help for UI changes.

Conventions are described in [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md#conventions). In short: English code and comments, no hard-coded UI strings, analysis returns data and `Text` renders it, and never parse event message texts.

## Code of conduct

Be kind and constructive. Harassment or personal attacks aren't tolerated. Maintainers may remove comments and block users who break this rule.

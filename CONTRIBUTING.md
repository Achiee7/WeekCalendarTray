# Contributing

Thanks for helping improve Week Calendar Tray. Keep changes focused, Windows-friendly, and careful with users' private calendar data.

## Prerequisites

- Windows 10 or Windows 11.
- .NET 8 SDK.
- PowerShell 7 or Windows PowerShell 5.1 for the repository scripts.
- Visual Studio 2022 is optional; the command-line SDK is sufficient.

## Build And Check

Run these commands from the repository root:

```powershell
dotnet restore .\WeekCalendarTray.sln
dotnet build .\WeekCalendarTray.sln -c Release --no-restore
dotnet run --project .\tests\WeekCalendarTray.SmokeTests\WeekCalendarTray.SmokeTests.csproj -c Release --no-build
dotnet run --project .\tests\WeekCalendarTray.UiTests\WeekCalendarTray.UiTests.csproj -c Release --no-build -- --artifacts=artifacts/ui
```

Both test projects are executable smoke-test harnesses. A successful `dotnet build` alone does not run them. The UI/layout harness writes synthetic screenshots to `artifacts\ui` when passed the documented argument.

For UI work, also launch the app from a local checkout and exercise the affected popup/settings flow. Avoid using a UNC-hosted executable for runtime validation.

## Change Guidelines

- Preserve the existing split between platform-neutral logic in `WeekCalendarTray.Core` and Windows UI/integration code in `WeekCalendarTray`.
- Add focused smoke coverage for calendar math, parsing, persistence recovery, or other logic that can run without desktop interaction.
- Catch and report failures at user or timer boundaries. Do not discard diagnostic context, and do not put private payloads into logs.
- Keep remote calendars read-only. Local-event behavior must remain clearly distinct from provider data.
- Treat acrylic and other Windows composition effects as optional; the solid themed UI must remain usable when an API is unavailable.
- Keep PowerShell file operations limited to computed, verified app/repository paths. Stage output before replacing a working directory.

## Test Data And Privacy

Never commit real iCal/ICS URLs, calendar exports, event details, email addresses, typed home/work addresses, coordinates, user settings, caches, logs, tokens, or certificates. Use small synthetic fixtures with example domains and fictional content.

Before opening an issue or pull request, review patches and screenshots for private calendar links. Those URLs often grant read access without a separate password.

## Pull Requests

Describe the user-visible behavior, failure modes considered, and commands actually run. Do not state that checks pass unless they were run against the final integrated files. Link relevant issues where available, and keep unrelated formatting or generated-output churn out of the change.

Release packaging is not required for every code change, but changes to installation, persistence, startup, diagnostics, or publishing should be validated with the staged release flow in `docs\RELEASING.md`.

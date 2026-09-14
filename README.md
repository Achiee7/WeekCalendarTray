# Week Calendar Tray

Week Calendar Tray is a lightweight Windows tray calendar with ISO week numbers, local events, read-only iCal/ICS subscriptions, and optional locally calculated prayer times. It runs beside the Windows clock and opens its own popup; it does not replace the built-in Windows calendar flyout.

The current version is **1.4.0**, following the merged 1.3.0 resizing update.

Version 1.4.0 unifies the expanded day agenda, prayer times, and event details in one side pane. The main calendar switches between Month and a scrollable Week timeline. `Today` returns to the current date without changing between Month and Week.

Performance refinements skip unchanged timeline renders, release hidden timeline visuals, reuse current-time markers and tray icons, and cache prayer timetables and notification settings. Notification polling stops when disabled. Only a visible Prayer pane needs a one-second UI timer; other open views use a 30-second check and the UI timer stops when hidden.

The `Glass tint` percentage controls the app's added tint, not the total opacity of Windows Desktop Acrylic. Windows supplies its own blur/material underneath. At low settings, buttons and grouped surfaces now follow the slider instead of retaining 76-82% minimum tint levels. Text and event colors remain opaque.

Version 1.2.2 prepares glass before the tray window is shown and re-creates the native backdrop once after opening. This corrects the solid first-open appearance without requiring prayer-panel or event interaction. The opacity slider now ranges from 20% to 95%, with the existing 75% default unchanged. First-open desktop pixel checks passed at 20% and 75% across three openings each.

Version 1.2.1 addresses the calendar turning solid when an event-details window receives focus. Borderless glass windows retain their active backdrop presentation without taking keyboard focus from another window. The native presentation hook is removed when glass is disabled or the window closes. Windows accessibility and platform fallback behavior still apply.

## Features

- Shows the current day and ISO week number in the tray, with date, week, and time in the tooltip.
- Opens a Monday-first calendar with month, year, and decade navigation.
- Switches between a Month grid and a Monday-first Week timeline using the main header controls. Timelines show all 24 hours, source-colored event blocks, a separate all-day row, overlapping appointments in separate columns, and a live current-time line on today.
- Expands a shared side pane for the selected day. Settings offers a List or Timeline day layout; selecting an event opens its details in that same pane, with a back action to return to the day.
- Adds a Prayer tab to the side pane only when prayer times are enabled. The expand arrow remains available when prayer times are off.
- Resizes by dragging the popup top or left edge and remembers the size across restarts.
- Creates and deletes events stored only on this PC.
- Reads one or more Google Calendar, Outlook, Microsoft 365, or other calendars through private iCal/ICS links. Synced events remain read-only.
- Displays event details when supplied by a feed, including description, organizer, location, and source link.
- Uses source-colored agenda accents and segmented day indicators so days containing multiple calendars remain distinguishable.
- Follows the Windows light/dark app theme and offers an optional Windows-native glass/acrylic backdrop with a solid-color fallback.
- Optionally displays locally calculated prayer times, a next-prayer countdown, and tray notifications.
- Starts as a single tray instance and records rotating metadata-only diagnostic logs.

## 1.2.0 UI Changes

Version 1.2.0 makes native backdrop initialization deterministic, adds a cleaner Settings layout, provides `System`, `Light`, and `Dark` theme modes, improves tooltip placement, and makes event-details lifetime, focus, and long-content layout predictable. `System` is the default theme mode and follows the Windows app theme.

“Glass” here means Windows desktop composition with backdrop, blur/tint, controlled opacity, and a fallback. It can evoke a glass surface, but it is not an exact implementation of Apple’s refractive Liquid Glass rendering. The opacity setting affects window surfaces, not text, icons, event content, or control opacity.

All three theme modes use the current Windows accent color for general application emphasis. Windows accent does not replace calendar identity colors: agenda accents and segmented day indicators continue using their stable per-source colors. Glass surface opacity defaults to 75% and can be adjusted from 20% through 95%.

Theme, glass, and opacity changes preview immediately in Settings. `Save` persists the selected appearance; `Close` without saving restores the previously saved appearance.

The Release build completed with zero warnings and errors, and both smoke-test executables passed. UI coverage includes 20 native-handle popup reopen cycles, singleton detail-window lifecycle, tooltip geometry and dismissal, long detail/footer layout, theme forcing and persistence, invalid-theme fallback, and accent contrast. Dark and light Settings screenshots were reviewed, including the custom-themed scrollbars.

Live theme and opacity preview, Close reversion, the theme dropdown, and scrolled prayer settings also passed automated checks. The native desktop pixel test was skipped because screen capture could not see the synthetic backdrop. Actual blur, display sleep/resume, Remote Desktop, scaling/display changes, Windows 10 fallback, and historical native crash resolution remain unverified for this release.

## Requirements

- Windows 10 or Windows 11 on x64 hardware.
- No .NET installation is needed for the packaged self-contained build.
- The .NET 8 SDK is required only when building from source.
- Internet access is required for iCal synchronization and typed-location geocoding.

## Install

Prefer the per-user installation instead of running `WeekCalendarTray.exe` from a network share. Extract the release zip to a local folder, open PowerShell in the extracted `WeekCalendarTray` folder, and run:

```powershell
.\Install-WeekCalendarTray.ps1
```

The installer stages the package before replacing an existing installation, installs to `%LOCALAPPDATA%\WeekCalendarTray\App`, creates a Start Menu shortcut, and launches the app. It enables Start with Windows by default.

Useful install options:

```powershell
# Keep the existing Start with Windows choice during an update.
.\Install-WeekCalendarTray.ps1 -PreserveStartup

# Disable Start with Windows, or install without launching.
.\Install-WeekCalendarTray.ps1 -NoStartup
.\Install-WeekCalendarTray.ps1 -NoLaunch
```

Windows may initially place the tray icon in the overflow area. Use Windows taskbar settings to keep it visible if desired.

## Use Calendars

Open `Settings...`, add a friendly calendar name, and paste a private iCal/ICS URL. Google calls this the `Secret address in iCal format`. For an Outlook-published calendar, use the `ICS` link rather than the `HTML` link.

Private calendar URLs act like access keys. Do not include them in screenshots, logs, bug reports, source control, or public issues.

Synced calendars are read-only: create, edit, and delete those events in their source calendar. Week Calendar Tray can separately create and delete local events stored in `%APPDATA%\WeekCalendarTray\local-events.json`.

## Prayer Times

Enable prayer times in `Settings...` and type a city or address. Expand the side pane and select its Prayer tab to see the timetable and countdown; Day returns to the selected date's agenda. Geocoding occurs only when you click `Find`, or when you click `Save` after changing the typed location. The app sends the typed query to OpenStreetMap Nominatim and stores the returned coordinates. It does not use GPS.

Prayer times are then calculated locally from the saved coordinates by a custom astronomical/seasonal approximation intended to stay near the published Koran.nl-style timetable behavior in the Netherlands. The app does **not** extract live times from Koran.nl. Results can differ from a mosque timetable, and the calculation currently uses the local timezone configured on the PC rather than deriving a timezone from the saved coordinates.

## Data And Diagnostics

The app stores per-user data in these locations:

- `%APPDATA%\WeekCalendarTray`: settings, private iCal links, synced-event cache, and local events.
- `%LOCALAPPDATA%\WeekCalendarTray\App`: installed application files.
- `%LOCALAPPDATA%\WeekCalendarTray\Logs`: rotating metadata-only diagnostic logs.

Settings and local data are written through backup-aware persistence. Invalid data is retained as `.corrupt-*` evidence where possible instead of being silently overwritten. Diagnostics omit calendar URLs, event contents, coordinates, and other private payloads by design. Native process failures may appear only in Windows Event Viewer.

Normal updates replace installed application files while preserving settings, read-only iCal subscriptions, local events, caches, logs, and the existing startup preference. Synced iCal/ICS calendars remain read-only by default; local events remain a separate on-device feature.

See [Troubleshooting](docs/TROUBLESHOOTING.md) for crash and support guidance.

## Build From Source

From the repository root on Windows:

```powershell
dotnet restore .\WeekCalendarTray.sln
dotnet build .\WeekCalendarTray.sln -c Release --no-restore
dotnet run --project .\tests\WeekCalendarTray.SmokeTests\WeekCalendarTray.SmokeTests.csproj -c Release --no-build
dotnet run --project .\tests\WeekCalendarTray.UiTests\WeekCalendarTray.UiTests.csproj -c Release --no-build -- --artifacts=artifacts/ui
dotnet run --project .\src\WeekCalendarTray\WeekCalendarTray.csproj -c Release --no-build
```

The smoke tests are a console executable, not a `dotnet test` project.

Create a staged self-contained release package with:

```powershell
.\scripts\Publish-Release.ps1
```

Output is written to `dist\WeekCalendarTray` and `dist\WeekCalendarTray-win-x64.zip` only after staging and package checks succeed. The zip contains a top-level `WeekCalendarTray` folder. See [Contributing](CONTRIBUTING.md) and [Releasing](docs/RELEASING.md) for the full workflows.

## Uninstall

Run the uninstall script from the release package or installed app folder:

```powershell
.\Uninstall-WeekCalendarTray.ps1
```

Settings, local events, caches, and logs are retained by default. Add `-RemoveUserData` to remove those per-user files as well.

## Limitations

- The app does not replace the Windows clock or calendar flyout.
- iCal/ICS synchronization is read-only and refreshes a rolling window rather than using incremental sync tokens.
- Calendar providers may revoke links or disable calendar publishing.
- Prayer times are a local approximation, not a live timetable service.
- Prayer calculations use the PC timezone, so coordinates in another timezone can produce incorrect local times.
- Windows-native glass is platform-specific and does not reproduce Apple Liquid Glass refraction exactly.
- Version 1.2.0 has not yet been validated across display sleep/resume, Remote Desktop, or scaling/display transitions.
- Release packages currently target `win-x64` and are not code-signed.
- Native crash reports observed while running from a network share cannot be declared fixed without longer local-install observation.

## License

This repository currently has no license file or license grant.

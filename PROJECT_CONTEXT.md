# Week Calendar Tray Project Context

## Purpose

Week Calendar Tray is a .NET 8 WPF notification-area utility for Windows. It presents a compact Monday-first calendar with ISO week numbers, local events, read-only iCal/ICS subscriptions, and optional prayer times. It is a separate tray application and does not replace the supported Windows clock/calendar flyout.

## Release State

- Current release version: `1.4.0`, prepared on 2026-09-15 after integrating the merged `1.3.0` resizing/day-view work and performance improvements.
- The shared side pane defaults to selected-day content (List or Timeline, saved as `DayViewLayout`), offers Prayer only when enabled, and displays event details inline. The main Month/Week control preserves Today as date navigation. Week columns resize with the popup and scroll horizontally below their readable minimum.
- Release build, core smoke tests, and UI checks pass for the development update. Rendered layout artifacts are in `C:\tmp\calendar-1.4.0`; these contain synthetic events only. New native desktop blur tests were not run for this update.
- Performance follow-up: unchanged timeline renders are skipped; hidden timeline visuals are released; marker updates reuse elements (0 bytes allocated across 1000 same-minute updates in the regression test). Prayer settings/timetables and tray icons are cached with invalidation; disabled notifications stop polling. Updated tests pass in `C:\tmp\calendar-1.4.0-perf`. The attempted first-open desktop capture was skipped because its background could not be reliably observed.
- The slider is now labeled Glass tint: it controls the app tint over Windows Desktop Acrylic, not total composited opacity. Low-setting control/group opacity floors were removed. The Windows material and its fallback remain unchanged.
- Version 1.2.2 prepares native glass before tray Show and resets/reapplies the backdrop once at dispatcher idle per opening. Desktop first-open tests passed at 20% and 75% for three openings each, without panel interaction. Opacity now ranges from 20% to 95%.
- Version 1.2.1 addresses inactive calendar glass while event details is active. A borderless-window WM_NCACTIVATE presentation hook leaves input activation and keyboard focus untouched and is detached on disable/close. Native desktop pixel verification still depends on an accessible desktop.
- The 1.2.0 package and local installation were verified; settings and local-event hashes and startup preference were preserved.
- Runtime target: `win-x64`.
- Package type: self-contained, single-file executable plus PowerShell installer/uninstaller and documentation.
- The 1.1.0 work includes persistence hardening, single-instance behavior, metadata-only diagnostics, guarded background operations, optional acrylic, segmented event indicators, and GitHub build/release infrastructure.
- Version 1.1.1 corrects the acrylic surface tint, removes duplicated WPF window shadows, and explicitly rounds the native window frame. Popup menus keep their opaque background.
- Version 1.2.0 delivers deterministic first-show backdrop setup, a clean Settings redesign, three-mode theme selection, surface opacity, Windows accent integration, tooltip remediation, and coherent event-details layout/lifetime.
- The 1.2.0 issue matrix separates implementation from validated evidence. Desktop pixel blur, display sleep/resume, Remote Desktop, scaling/display changes, Windows 10 fallback, and historical native crash resolution remain open.

Generated outputs under `bin`, `obj`, and `dist` are disposable and excluded from source control. The preferred runtime location is `%LOCALAPPDATA%\WeekCalendarTray\App`, installed from a local extracted release package.

## User Experience

- The app starts without a normal taskbar window and keeps one tray instance per interactive session.
- Left-clicking the tray icon toggles the calendar popup. The tray menu provides open, today, sync, settings, and exit actions.
- The calendar supports month, year, and decade navigation, Monday-first rows, ISO week numbers, today/selection highlighting, and a selected-day agenda.
- Users can create and delete local events. Local events are stored only on the PC.
- Synced iCal/ICS events are read-only. Their details can include time, calendar, location, organizer, description, and source URL when published by the provider.
- Source-colored agenda accents and segmented day indicators distinguish calendars with events on the same day.
- Appearance offers `System`, `Light`, and `Dark`; `System` is the default and follows the Windows app theme. Every mode uses the Windows accent color. Glass surface opacity defaults to 75%, ranges from 20% through 95%, and does not change text opacity or calendar/source event colors.
- Settings previews theme, glass, and surface-opacity changes immediately. `Save` persists them; closing without saving restores the previous saved appearance.
- Prayer times can be enabled in Settings and displayed in an expandable panel with a live next-prayer countdown and optional tray notifications.

## Architecture

Solution: `WeekCalendarTray.sln`

Projects:

- `src\WeekCalendarTray`: WPF/Windows Forms tray app targeting `net8.0-windows`.
- `src\WeekCalendarTray.Core`: calendar, iCalendar, and prayer calculation logic targeting `net8.0`.
- `tests\WeekCalendarTray.SmokeTests`: package-light console smoke-test executable targeting `net8.0`.
- `tests\WeekCalendarTray.UiTests`: Windows UI/layout smoke-test executable targeting `net8.0-windows`.

Important application components:

- `TrayApplicationController`: notification icon, menus, popup lifecycle, timers, synchronization, and prayer notifications.
- `MainWindow`: calendar navigation, month/week selection, unified day/prayer/details pane, local-event UI, and event indicators.
- `TimelineView` and `TimelineLayout`: shared day/week rendering, 24-hour scroll area, all-day entries, clipped overnight events, overlap-column assignment, source colors, and current-time indicator. Layout uses the existing parsed calendar events and PC local timezone; it does not change synchronization or recurrence parsing.
- `SettingsWindow`: clean grouped settings UI, `System`/`Light`/`Dark` selection, live appearance preview, native-glass surface opacity, explicit `Save`/`Close`, startup, prayer settings, and typed-location workflow.
- `CalendarSyncCoordinator`: settings, iCal fetching, cache updates, and background sync coordination.
- `LocalCalendarEventStore`: local event persistence and deletion.
- `AtomicJsonFile`: backup-aware JSON writes and `.bak`/`.corrupt-*` recovery behavior.
- `AppDiagnostics`: rotating metadata-only diagnostics under `%LOCALAPPDATA%\WeekCalendarTray\Logs`.
- `AppPaths`: known per-user settings, cache, local-event, installation, and diagnostic locations.
- `PrayerLocationResolver`: on-demand OpenStreetMap Nominatim lookup for typed locations.
- `PrayerTimesCalculator`: custom local astronomical and seasonal approximation.
- `ThemeManager`: light/dark resources, Windows accent integration, surface opacity, and Windows backdrop fallback behavior.

Core iCalendar parsing uses `Ical.Net` and supports timed, all-day, and recurring events within the configured rolling window.

## Calendar Data

Supported subscription schemes are `http`, `https`, and `webcal`; `webcal` is normalized for fetching. Multiple subscriptions are supported. The default rolling window is 30 days in the past and 180 days in the future, and background synchronization is scheduled every 15 minutes.

Private iCal links are bearer-style secrets. They must never be committed, pasted into public issues, or included in diagnostic output. Synced content is cached locally for a responsive popup. Calendar event creation, editing, and deletion in the remote provider are not supported.

Local events are a separate feature. Users can create timed or all-day entries and delete them from the details window. They are saved in `%APPDATA%\WeekCalendarTray\local-events.json` and are not uploaded to a calendar provider.

## Prayer Times

The location workflow is typed and explicit:

1. Enter a city or address.
2. Click `Find`, or click `Save` after changing the typed value.
3. Nominatim resolves the query and the app stores the returned name and coordinates.
4. Subsequent daily prayer times are calculated locally.

The app does not use GPS. It also does not perform live extraction from Koran.nl. The calculation is a custom approximation intended to remain near the Koran.nl-style Netherlands timetable: solar sunrise/sunset and noon, standard Asr, a seasonal high-latitude Fajr adjustment, and a seasonal Isha offset. A local mosque timetable remains authoritative where it differs.

The calculator currently receives the PC's local timezone. It does not derive a timezone from the saved coordinates, so a location in another timezone can produce incorrect displayed times.

## Persistence And Diagnostics

Settings, event cache, and local events are per-user files. Writes use staging/backup behavior. When invalid JSON cannot be recovered from a backup, the invalid file is retained with a `.corrupt-*` name where possible rather than silently overwritten.

User-facing operations catch expected failures and report a status instead of allowing routine background work to terminate the process. Rotating diagnostics are metadata-only and belong under `%LOCALAPPDATA%\WeekCalendarTray\Logs`; they must not contain private calendar URLs, event bodies/titles, typed addresses, or coordinates.

Managed diagnostics cannot capture every native process termination. Windows Event Viewer remains the source for native fault codes and faulting modules.

## Native Crash Evidence

Sanitized Windows Application log review found several native crashes while the executable was running from a UNC share. One recent event reported `0xc0000006` (`EXCEPTION_IN_PAGE_ERROR`) in a Windows module; Microsoft documents that this status can occur when a network connection is lost while a program is running over a network. Other events reported native access violations or stack overflows with endpoint-security modules in the faulting stack.

This evidence supports preferring a local installed copy, but it does not establish a single root cause. A faulting native module does not prove that the app or the security product caused the failure, and the absence of matching .NET exception records does not prove the managed code was uninvolved. The incidents cannot be called fixed from historical logs alone. Verification should continue against locally installed builds under `%LOCALAPPDATA%`.

Do not disable, exclude, or reconfigure endpoint protection as a workaround. See `docs\TROUBLESHOOTING.md` for a privacy-conscious collection procedure.

## 1.2.0 Video/UI Remediation

The privacy-safe issue matrix is `docs\VIDEO_UI_REMEDIATION_STATUS_2026-09-10.md`. It maps `CAL-UI-001` through `CAL-UI-010` to implementation and separately recorded validation. The original video report remains local-only because it contains private recording and event details and is excluded by `.gitignore`.

Product boundaries for this work:

- Windows native glass means desktop backdrop, blur/tint, controlled surface opacity, and fallback behavior. It does not reproduce Apple Liquid Glass refraction exactly.
- Surface opacity defaults to 75%, is constrained to 20%-95%, and does not fade text, icons, controls, or event content.
- `System` is the default theme and follows Windows; explicit `Light` and `Dark` modes override brightness only. All three modes still use the Windows accent color.
- Appearance changes preview immediately. `Save` commits them, while `Close` without saving restores the last saved appearance.
- Windows accent color affects general application emphasis, not stable calendar/source colors in agenda accents or segmented day indicators.
- Synced iCal/ICS data remains read-only; local events remain separate.
- Application updates preserve existing user data and startup preference under the established installer defaults.
- Verified automation does not establish desktop-composited pixel blur. Sleep/resume, RDP, scaling/display transitions, Windows 10 fallback, and historical crash resolution remain open.

## Build And Release

Portable commands from any local clone:

```powershell
dotnet restore .\WeekCalendarTray.sln
dotnet build .\WeekCalendarTray.sln -c Release --no-restore
dotnet run --project .\tests\WeekCalendarTray.SmokeTests\WeekCalendarTray.SmokeTests.csproj -c Release --no-build
dotnet run --project .\tests\WeekCalendarTray.UiTests\WeekCalendarTray.UiTests.csproj -c Release --no-build -- --artifacts=artifacts/ui
.\scripts\Publish-Release.ps1 -Runtime win-x64
```

`Publish-Release.ps1` publishes into a temporary directory, verifies required files, and only then swaps the staged folder and zip into `dist`. Native `dotnet` exit codes are checked explicitly. The package includes the installer, uninstaller, README, changelog, and troubleshooting guide.

GitHub Actions builds on `windows-latest`, runs the smoke-test executables, publishes the package as a workflow artifact, and creates a GitHub release only for semantic version tags shaped like `v1.2.0`. Release creation uses the repository `GITHUB_TOKEN`; no additional release secret is required.

## 1.2.0 Verification On 2026-09-10

- Integrated Release solution build completed with zero warnings and zero errors.
- Core smoke tests passed.
- UI tests passed, including 20 popup create/show/close/recreate cycles with new native handles and backdrop-state checks.
- UI tests passed for singleton event details, hiding/navigating lifecycle, tooltip geometry and dismissal, long detail/footer layout, `System`/`Light`/`Dark` forcing, settings-store persistence, invalid theme fallback, and accent contrast.
- Dark and light Settings screenshots were reviewed, including custom-themed scrollbars.

The final native desktop pixel test correctly skipped because screen capture could not see the synthetic backdrop. Actual blur remains unverified. Additional live theme/opacity preview and Close rollback tests passed; dropdown and scrolled prayer settings renders were reviewed.

The self-contained 1.2.0 package was published and inspected. Local installation succeeded after retrying a temporary file lock; the executable reports 1.2.0.0 and is running responsively. Settings and local-event hashes were unchanged and startup preference was preserved. Sleep/resume, Remote Desktop, scaling/display changes, Windows 10 fallback, and historical intermittent native crashes remain open.

## Verified 1.1.1 Baseline On 2026-09-09

The 1.1.1 acrylic correction was also checked using actual desktop-composited captures over a synthetic two-color background. Mean channel difference when changing the background was 78.91 with acrylic enabled and 0 with it disabled. Native screenshots showed rounded outer corners, and the surface resource was separated from opaque menu resources. Run the UI test executable with the additional --native-visual argument on an interactive Windows desktop to repeat this check; unattended CI runs the remaining layout/native-state checks without screen capture.

- Integrated Release solution build passed with zero warnings and zero errors.
- Core smoke tests and Windows UI tests passed, including persistence recovery, concurrent access, segmented markers, long-text layout, native acrylic enable/disable, and closing/reopening the calendar.
- Synthetic light/dark screenshots were visually reviewed without exposing personal calendar data.
- The self-contained 1.1.1 package was published and installed locally. Settings and local-event file hashes were unchanged; the enabled startup entry was migrated to the local executable.
- A second launch exited successfully and left one running instance. The initial local run was responsive with no new application crash events.
- GitHub Actions configuration was prepared and reviewed, but has not run on GitHub. No repository was created or pushed.
- Longer observation is still required for the historical intermittent native crashes.

These results belong to the 1.1.1 baseline. They do not establish that the 1.2.0 video/UI remediation criteria pass.

## Known Limitations

- Windows may place a new tray icon in the overflow area.
- iCal publishing may be disabled or links may be revoked by a provider.
- There are no incremental provider sync tokens or remote event mutations.
- Prayer times are approximations and depend on the PC timezone.
- Windows native glass is not an exact reproduction of Apple's refractive Liquid Glass.
- Desktop-composited pixel blur, display sleep/resume, Remote Desktop, scaling/display transitions, and Windows 10 fallback are not yet validated for 1.2.0.
- The current package is x64-only and unsigned.
- Initial local-install checks passed, but intermittent native crash resolution remains unverified.

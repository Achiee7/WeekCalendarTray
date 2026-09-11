# Changelog

All notable changes to Week Calendar Tray are documented here. The format follows Keep a Changelog, and releases use semantic versioning.

## [Unreleased]

### Added

- Compact `Month`/`Day` toggle in the popup header. `Day` hides the month grid and shows the selected date's full scrollable agenda, including each event's location.
- Day-aware navigation: in Day view the `<` and `>` buttons step one day at a time, and their tooltips describe the active step size.

### Changed

- Moved `<` and `>` into a bottom navigation strip beside the sync status, freeing the header in both views.
- The `Day` button becomes `Today` once Day view is active, so a second press jumps to today. This replaces the separate `Today` button.
- Day view drops its separate title row, which only repeated the date already shown in the header. Month view keeps both rows, since its title names the browsed month and is also the zoom-out to Year/Decade.
- The month title acts as a zoom-out from Day view to the month grid, matching the existing month/year/decade behavior.

### Fixed

- Run startup UI operations on the dispatcher. The window is built before the dispatcher loop starts, so there was no `DispatcherSynchronizationContext` to capture; continuations after the first `await` resumed on a thread-pool thread and threw `InvalidOperationException` on any UI access. This logged a "Calendar operation" failure on every launch through `LoadPrayerSettingsAsync` and left the same latent race in the cache refresh started from `RefreshCalendar`.

## [1.2.2] - 2026-09-11

### Fixed

- Initialize glass before the tray popup creates its native window and re-create the backdrop once after each opening. The calendar no longer needs prayer-panel or event interaction to refresh its initial solid fallback.
- Extend the opacity slider and persisted/native validation range down to 20%; retain the 95% maximum and 75% default.
- Add a desktop-composited first-open regression test through the real tray-controller path, without resizing or opening child panels. Verified three openings each at 20% and 75%.

The first-open visual regression passed on the accessible Windows desktop. Sleep/resume, other display configurations, and long-term stability still require observation.

## [1.2.1] - 2026-09-10

### Fixed

- Keep the calendar's glass presentation consistent when event details receives focus, without intercepting input activation or stealing focus.
- Remove the native presentation hook when glass is disabled or a window closes.
- Exclude remote conversation attachments from source control.

Build, core smoke tests, and UI tests passed locally, including independently executed presentation-message policy and hook cleanup checks. Native visual coverage now includes the inactive calendar alongside an active owned details window.

Native desktop pixels still require verification on an accessible interactive desktop. Inaccessible captures are reported as skipped, not as successful blur tests.

## [1.2.0] - 2026-09-10

### Added

- Added `System`, `Light`, and `Dark` theme choices, defaulting to `System`; every mode uses the Windows accent color.
- Added adjustable glass surface opacity from 35% through 95%, defaulting to 75%, without fading text or content.
- Added UI regression coverage for repeated native-handle popup recreation, detail-window lifecycle, tooltip behavior, long-content layout, theme forcing and persistence, invalid-theme fallback, and accent contrast.

### Changed

- Redesigned Settings with clearer grouped controls, explicit `Save`/`Close` actions, and custom theme-aware scrollbars.
- Applied the selected theme and Windows accent consistently across application chrome while preserving per-calendar/source event colors.
- Retained live theme/glass/surface-opacity preview: `Save` persists it, while closing without saving restores the previous appearance.
- Coordinated tint/theme treatment across the calendar, prayer panel, Settings, and event details while retaining readable opaque controls and popups where appropriate.

### Fixed

- Applied native backdrop configuration during first show and later popup recreation.
- Reused one modeless event-details window and kept hide/navigation/ownership behavior coherent.
- Constrained, wrapped, repositioned, and promptly dismissed event-preview tooltips.
- Improved event-details layout for long titles, metadata, links, and footer actions.

### Verification

- Release solution build completed with zero warnings and zero errors.
- Core smoke tests passed.
- UI tests passed, including 20 native-handle reopens, singleton details and hide/navigation lifecycle, tooltip geometry/dismissal, long detail/footer layout, `System`/`Light`/`Dark` forcing, settings-store persistence, invalid-value fallback, and accent contrast.
- Dark and light Settings screenshots were reviewed; scrollbars use custom theme styling.

Live appearance preview and Close rollback tests passed, with dropdown and scrolled prayer settings renders reviewed. The native desktop pixel check was skipped because screen capture could not see the synthetic backdrop, so visible blur remains unverified. Sleep/resume, Remote Desktop, scaling/display changes, Windows 10 fallback, and historical native crash resolution remain open.

The material is Windows-native backdrop/tint with fallback behavior; it is not an exact implementation of Apple’s refractive Liquid Glass. Existing read-only iCal behavior, user data, and stable event-source colors remain unchanged by default.

## [1.1.1] - 2026-09-09

### Fixed

- Removed the opaque-looking window tint and duplicated WPF shadow that obscured the native acrylic backdrop.
- Applied rounded corners to the native window surface and suppressed its extra rectangular border.
- Scoped translucency to the window surface so overflow menus remain opaque.
- Added desktop-composited visual regression checks for acrylic rather than relying only on native attribute values.

## [1.1.0] - 2026-09-09

Build, smoke, package, and initial local-install verification completed. Historical intermittent native crashes still require longer observation.

### Added

- Single-instance coordination for the current interactive session.
- Rotating metadata-only application diagnostics under `%LOCALAPPDATA%\WeekCalendarTray\Logs`.
- Local timed and all-day event creation and deletion.
- Optional Windows acrylic backdrop with a themed solid fallback.
- Segmented, source-colored day indicators for dates containing events.
- Windows GitHub Actions build, smoke-test, package-artifact, and version-tag release automation.
- Contributor, release, privacy, and troubleshooting guidance.

### Changed

- Hardened JSON persistence with staged writes, `.bak` recovery, and preservation of invalid data as `.corrupt-*` evidence where possible.
- Guarded user-facing and background operations so expected failures are reported and logged rather than escaping routine async paths.
- Reworked publish and install scripts to stage packages before replacement and validate recursive operation targets.
- Added installer support for preserving the existing Start with Windows preference during upgrades.
- Clarified that synced iCal calendars are read-only while local events can be created and deleted.
- Clarified that prayer times are calculated locally, are not extracted live from Koran.nl, and use the PC's configured timezone.
- Prefer the installed `%LOCALAPPDATA%` copy over executing the application from a network share.

### Fixed

- Persistence failure paths no longer silently overwrite unreadable primary data when recovery evidence can be retained.
- Multiple app launches no longer intentionally create concurrent tray instances in the same session.
- Reopening the popup after closing it with `Alt+F4` now creates a fresh window instead of asking WPF to show the already-closed instance.

## [1.0.0] - 2026-05-26

### Added

- Initial .NET 8 WPF tray calendar with ISO week numbers and month navigation.
- Read-only iCal/ICS synchronization for multiple subscriptions.
- Timed, all-day, and recurring event parsing with details and local caching.
- Windows light/dark theme following, event markers, and hover previews.
- Optional local prayer-time calculation, countdown, and notifications.
- Per-user installer, startup option, and uninstaller.

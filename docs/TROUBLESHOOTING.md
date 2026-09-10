# Troubleshooting

## App Exits Or Disappears

Install and run the app from the per-user local path:

```text
%LOCALAPPDATA%\WeekCalendarTray\App\WeekCalendarTray.exe
```

Avoid treating a network share as the normal execution location. Microsoft documents that `EXCEPTION_IN_PAGE_ERROR` can occur when Windows cannot load a required memory page, including when a network connection is lost while a program is running over a network. This makes local installation a sensible mitigation, not proof of a particular root cause.

Sanitized Windows Application log review found several historical native crashes during UNC execution, including `0xc0000006`, access violations, and stack overflows. Some events named endpoint-security modules as faulting modules. Those records do not establish whether the app, the security product, the network, or an interaction between them caused the failure. They also cannot confirm that a newer build fixed it.

Do not disable or reconfigure antivirus or endpoint protection. Reproduce from the local installed copy and collect evidence for the application maintainer or IT/security team.

One separate managed lifecycle defect is fixed in 1.1.0: closing the popup with `Alt+F4` left the controller holding a closed WPF window, so a later tray click attempted to show that unusable instance. The controller now clears the reference when the popup closes and creates a fresh window on the next open. That concrete fix should not be conflated with the historical native UNC/endpoint-security crash evidence above.

## Diagnostics

Application diagnostics are stored under:

```text
%LOCALAPPDATA%\WeekCalendarTray\Logs
```

The logs rotate and contain operational metadata rather than calendar URLs, event content, typed addresses, or coordinates. Managed exception logs may be absent for native terminations.

For a native exit, open **Event Viewer**, go to **Windows Logs > Application**, and find entries at the failure time. Record only:

- Timestamp and Windows version.
- Week Calendar Tray version.
- Whether the executable path was local or UNC.
- Exception code, faulting module name, and fault offset.
- Whether the app metadata log stopped abruptly or recorded an operation failure.

Redact usernames, calendar links, event titles/descriptions, organizer details, addresses, coordinates, and unrelated machine/security data before sharing publicly.

Microsoft exception-code reference: <https://learn.microsoft.com/en-us/windows/win32/debug/getexceptioncode>

## Settings Or Events Cannot Be Loaded

Per-user calendar data is stored under `%APPDATA%\WeekCalendarTray`. Persistence uses `.bak` files and retains unrecoverable invalid data as `.corrupt-*` files where possible. Do not delete these files before making a private backup if recovery matters.

Check the app status message and metadata log. A `.corrupt-*` file is evidence that the original could not be parsed; it is not automatically proof of disk corruption or an application defect.

Private iCal links should never be pasted into a public issue. If a subscription fails, report the provider type, HTTP status category if shown, and whether a newly generated private link works.

## Glass, Opacity, Or Accent Looks Wrong

Week Calendar Tray uses Windows-native desktop composition. Its glass mode combines the available Windows backdrop with application tint, surface opacity, and a solid fallback; it is not an exact implementation of Apple's refractive Liquid Glass effect.

- The opacity setting changes top-level window surfaces. It should not fade text, icons, controls, or event content.
- Theme choices are `System`, `Light`, and `Dark`; `System` is the default and follows the Windows app theme.
- Every theme mode uses the Windows accent color for general application emphasis. It should not recolor the stable per-calendar/source agenda accents or segmented day indicators.
- Appearance changes preview immediately in Settings. Use `Save` to persist them; `Close` without saving should restore the previous saved appearance.
- Opaque inputs, menus, or popups can be intentional readability layers rather than a failed backdrop.
- Report whether the problem occurs on first show, reopen, Settings/details open, prayer expansion, transparency-disabled mode, or high contrast.
- Sleep/resume and Remote Desktop transitions are not considered validated unless a test report explicitly records them.

## Prayer Location Or Times Are Wrong

- Location lookup needs internet access only when `Find` is clicked or `Save` is clicked after changing the typed location.
- The app uses OpenStreetMap Nominatim for that lookup and does not use GPS.
- Prayer times are calculated locally; they are not downloaded from or extracted live from Koran.nl.
- The current calculation uses the PC timezone. Confirm Windows date, time, timezone, and daylight-saving settings, especially when the saved coordinates are outside the PC's timezone.
- Local mosque timetables can differ from the approximation and should be followed where authoritative.

## Tray Icon Is Missing

Windows may place newly installed tray icons in the overflow area. Open the overflow menu first, then use Windows taskbar settings to keep Week Calendar Tray visible. Starting the app again should activate the existing instance rather than create another tray process in the same session.

## Reporting A Bug

Use the GitHub bug form and include exact reproduction steps, the app version, and the checks you actually performed. State whether the app was installed locally. Attach only sanitized metadata logs and sanitized Event Viewer fields.

# Video/UI Remediation Status

**Release:** 1.2.0  
**Date:** 2026-09-10

## Scope

This is the privacy-safe status companion to the local video review. It uses issue IDs only and contains no recording filename, personal event data, or calendar content.

Status meanings:

- **Validated:** the reported automated or visual check directly covers the stated behavior.
- **Partial:** implementation or related checks passed, but part of the original visual/environment acceptance criteria remains open.
- **Open:** no sufficient 1.2.0 acceptance evidence has been recorded.

## Verification Summary

- Integrated Release solution build completed with zero warnings and zero errors.
- Core smoke tests passed.
- UI tests passed for 20 native-handle popup reopens, singleton detail-window lifecycle, hide/navigation behavior, tooltip geometry and dismissal, long detail/footer layout, theme forcing and persistence, invalid theme fallback, and accent contrast.
- Dark and light Settings screenshots were reviewed. The redesigned window and custom theme-aware scrollbars were visible without reported overlap.
- Live System/Light/Dark and opacity preview, Close rollback, and scrolled prayer settings passed additional tests. The theme dropdown, prayer settings, expanded calendar, and padded detail actions were visually reviewed from WPF renders.
- The self-contained 1.2.0 package was published and its zip contents inspected. Local installation completed after retrying a temporary file lock. The installed executable reports 1.2.0.0 and is running responsively from the local App directory. Settings and local-event SHA-256 hashes are unchanged, and the startup preference was preserved.

The final native desktop pixel test correctly skipped because capture could not see the synthetic backdrop. Earlier captures contained only wallpaper. No visible-blur, diffusion, or background-response claim is based on those captures.

## Issue Matrix

| Issue | 1.2.0 remediation | Recorded validation |
| --- | --- | --- |
| `CAL-UI-001` | Apply the native Windows backdrop during first show and reapply it predictably after recreation. | **Partial.** UI tests passed 20 create/show/close/recreate cycles with new native handles and backdrop-state checks. Actual desktop-composited pixels, sleep/resume, RDP, scaling/display transitions, and Windows 10 fallback remain open. |
| `CAL-UI-002` | Use controlled surface opacity and stable tint/readability. Opacity defaults to 75%, ranges from 35% through 95%, and does not fade text/content. | **Partial.** Opacity state, live preview, Close rollback, and accent contrast passed. Visible blur/background diffusion was not captured. |
| `CAL-UI-003` | Coordinate the prayer and calendar areas as one outer material with a restrained separator/tint hierarchy. | **Partial.** Expanded WPF render reviewed: shared surface and separator are coherent. Desktop-composited material remains unverified. |
| `CAL-UI-004` | Provide a clean grouped Settings layout; `System`, `Light`, and `Dark`; live preview; explicit `Save`/`Close`; and themed scrollbars. | **Partial.** Theme forcing, store persistence, invalid fallback, accent contrast, preview cancellation, dropdown, and dark/light screenshots passed. Bright/patterned desktop composition remains open. |
| `CAL-UI-005` | Apply the shared theme/accent/material system to event details while keeping long content readable. | **Partial.** Long details/footer geometry passed. Desktop-composited detail-window backdrop and contrast over varied backgrounds were not verified. |
| `CAL-UI-006` | Reuse one modeless event-details window so repeated selections cannot stack independent details. | **Validated.** Singleton details lifecycle tests passed. |
| `CAL-UI-007` | Keep owned transient-window hide, close, navigation, and focus behavior coherent. | **Validated.** Hide/navigation lifecycle tests passed. |
| `CAL-UI-008` | Constrain, wrap, place, and dismiss event-preview tooltips without obstructing important content. | **Validated.** Tooltip geometry and context-dismissal tests passed. |
| `CAL-UI-009` | Adapt detail content and footer actions to long metadata without overlap or cramped controls. | **Validated.** Long detail/footer layout tests passed. |
| `CAL-UI-010` | Enforce a predictable singleton modeless-details workflow when selection or navigation changes. | **Validated.** Singleton and navigation state-transition tests passed. |

## Product Boundaries

- “Glass” means Windows-native desktop composition with backdrop, tint, controlled surface opacity, and fallback behavior. It is not an exact implementation of Apple’s refractive Liquid Glass rendering.
- Theme choices are `System`, `Light`, and `Dark`. `System` is the default and follows the Windows app theme; explicit modes override brightness while still using Windows accent.
- Surface opacity defaults to 75%, is constrained to 35%-95%, and affects top-level materials rather than text, icons, event content, or controls.
- Theme, glass, and opacity changes preview immediately. `Save` persists them; closing without saving restores the prior saved appearance. Dedicated preview and cancellation regression tests passed.
- Windows accent affects application chrome and general UI emphasis. It does not replace or remap stable per-calendar/source agenda accents or segmented day indicators.
- iCal/ICS subscriptions remain read-only. Local events remain separate from synchronized provider data.
- Application updates preserve existing user data. The 1.2.0 installation retained settings and local-event files byte-for-byte and preserved the enabled startup entry targeting the local executable.

## Remaining Work

- Obtain a valid interactive desktop capture before making any visible-blur claim.
- Exercise display sleep/resume, Remote Desktop connect/disconnect, scaling/display changes, Windows transparency/high contrast, and Windows 10 fallback.
- Continue longer observation for historical intermittent native process failures.

The original local report remains the evidence baseline and must not be published because it contains private source-recording and event details.

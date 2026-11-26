# Desktop Memory browser integration: Phase 8A

Status: diagnostic implementation only. No tab URLs, titles, profiles, cookies, history, page contents or snapshot changes. No browser launches, focus changes, tab modifications or window moves. The probe is not packaged in BranchWatch production releases.

Chromium native messaging passes a parent native window handle on Windows when called from an extension page (the service worker context may pass zero). A parent handle is a candidate association, not a documented general browser-window-ID to HWND mapping. The popup directly calls the native host with its focused normal window ID, geometry and random request nonce. The host checks the supplied native parent, reads process identity, native bounds, DPI and documented virtual desktop membership twice, rejects changed/unknown parents, and returns observations. The popup checks its own window before and after the call. No correlation is treated as authorization. Even exact bounds do not prove identity. Mixed DPI coordinate conversion remains unqualified.

## Windows test setup

Use a non-elevated interactive Windows session with .NET 10 and PowerShell 7. In Chrome open chrome://extensions (Edge: edge://extensions), enable developer mode, choose Load unpacked, and select the repository directory `tools/browser-correlation-extension`. Copy the exact extension ID displayed by the browser. The extension requests only nativeMessaging; incognito is disabled. No content scripts, background workers, host permissions or tabs permission.

From the repository root:

```powershell
pwsh -File scripts/browser-correlation-probe.ps1 -Action Register -Browser Chrome -ExtensionId YOUR_EXTENSION_ID
```

Repeat with Edge and its extension ID if testing Edge. This explicitly registers a per-user development native host, allowlisted to the specified extension ID; it refuses to replace existing registrations. It builds a standalone executable in LOCALAPPDATA. Open the extension's toolbar popup in a focused, normal window, and click Inspect this window. Results are shown in the popup only. A nonzero native parent is required. If the browser does not supply one in this context, record that as a failure; do not work around it with title guessing. Compare the returned HWND with the existing BranchWatch Phase 0 probe report on the same active desktop. Raw handles, desktop IDs and geometry are private diagnostic data.

## Required physical matrix

- Chrome and Edge separately: one normal window, two differently positioned windows, two identically positioned windows, repeated clicks on each popup. Verify each native parent maps to the intended physical window, including when bounds coincide.
- Two virtual desktops, multiple profiles, overlapping windows, moving a window between desktops, active desktop changes. Verify COM membership against Task View and compare the parent to the existing read-only BranchWatch probe.
- At 100% scaling compare browser bounds to native screen and visible-frame bounds. Repeat at mixed DPI, portrait orientation, negative coordinates and spanning windows. Record differences; do not apply guessed scaling.
- Verify maximized/minimized/private windows are not accepted by this normal-window diagnostic. Check failed host registration, wrong extension ID, missing or zero parent and window change during observation.
- Record browser version, Windows build and architecture, popup results and manual observations. No raw report sharing without review.

Phase 8B must not assume this popup technique automatically maps all windows on demand. In particular a service worker does not provide the same parent context. Qualify a profile-aware local messaging and background inventory strategy separately; ambiguous or unqualified windows must not contribute URLs to a snapshot.

## Cleanup

```powershell
pwsh -File scripts/browser-correlation-probe.ps1 -Action Unregister -Browser Chrome
pwsh -File scripts/browser-correlation-probe.ps1 -Action Unregister -Browser Edge
```

Remove the unpacked extensions in the browser UI and delete the local probe directories when finished. The script does not remove an unrelated registration.

References: [Chromium native messaging](https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging), [windows API](https://developer.chrome.com/docs/extensions/reference/api/windows), [Edge extension sideloading](https://learn.microsoft.com/en-us/microsoft-edge/extensions/getting-started/extension-sideloading).

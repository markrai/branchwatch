# Desktop Memory: Phase 8G Firefox support foundation

Firefox's documented native messaging invocation supplies the native manifest path and the extension ID, not Chromium's parent-window HWND. A matching Firefox foreground window is only a candidate: it does not establish which browser window ID or profile owns that native window. Geometry, title or process-name equality alone is insufficient. For this reason this phase deliberately implements connectivity and read-only diagnostics, not full Firefox session capture or restoration.

## Setup

Use Windows, PowerShell 7, .NET 10 and Firefox 128 or later. Build and run the current BranchWatch checkout. Register the separate diagnostic native host:

```powershell
pwsh -File scripts/firefox-companion.ps1 -Action Register
```

Open Firefox's about:debugging#/runtime/this-firefox page, choose Load Temporary Add-on, and select tools/firefox-companion-extension/manifest.json. The fixed development add-on ID is branchwatch-companion@branchwatch.local. The registry key is per user under HKCU\Software\Mozilla\NativeMessagingHosts\com.branchwatch.firefox_probe and the executable/manifest are stored under LOCALAPPDATA\BranchWatch\browser-companion\Firefox. No elevation is required. Temporary add-ons are removed at browser restart and may lose extension storage; a random profile identifier is not a stable browser account ID or authentication credential. No signed Firefox extension is distributed.

## Diagnostics

Open the Companion toolbar popup in a focused, normal, non-private Firefox window. Check connection uses the same bounded hello-only BranchWatch pipe as Chromium. Inspect foreground candidate sends the current extension window ID, geometry and nonce, with no URLs, tab titles or profile inventory. The host verifies its own manifest path, exact extension allowlist and executable path. For observation it reads the foreground HWND, confirms the expected Firefox native class/executable and stable process/window geometry and COM desktop membership, then verifies foreground has not changed. The popup checks its browser window identity and geometry before and after the request.

The response is always explicitly diagnostic-only. Even an exact geometry match at 100% scaling cannot establish association. A popup may steal focus, causing a rejected native candidate; that is an expected observation to record, not a reason to relax the checks. The foreground diagnostic uses neither tab permission nor content scripts, does not change native window state or browser titles, and does not write snapshots or transmit data remotely. It does not expose capture or restore operations. Chromium retains its existing parent-handle checks and restore protocol v2.

## Two-marker challenge (Phase 8G.2)

Reload the temporary add-on (0.2.0) and rebuild/re-register the Firefox native host. Open the toolbar popup in the intended normal window and choose **Open correlation test tab**. This explicitly creates one extension-owned active tab in that window. Select Run correlation challenge, keep the tab active and window focused, then close the test tab yourself. The extension changes only its own document title, not an existing tab or the window title preface. It does not read URLs or existing tab titles through the browser API. The native diagnostic checks the beginning of Firefox native window titles locally; raw titles are not returned.

The challenge uses two independent 128-bit random markers. The host first checks that neither is present, then verifies that each marker appears at the start of exactly one enumerated Firefox top-level window title. The candidate must be foreground, normal, have verified current desktop membership and stable native identity. The extension requires the same browser tab/window and geometry throughout, and the same native handle, process/thread identity, process creation time, desktop and bounds across both markers. It restores its own document title in a finally block and verifies that both markers are absent before reporting a positive diagnostic result. Unexpected focus, missing titles, multiple matches, native drift or failed cleanup reject the test. A browser/extension interruption may prevent cleanup; close the diagnostic tab. No existing tab is closed automatically.

Even a positive result is labelled challenge-correlated-not-authorized. It is a short-lived observation, not permission to capture or restore and not a persistent mapping. Native enumeration and browser state are not atomic. This implementation still requires real Firefox/Windows qualification before enabling URL operations. Do not weaken the marker check to a foreground or geometry guess if Firefox does not propagate the title as expected.

## Physical qualification

- Check the hello operation in Firefox with BranchWatch running and closed. Verify separate profile identifiers in independently installed profiles.
- Open two normal Firefox windows with different bounds, then identical bounds, then separate profiles. Inspect each popup and compare candidate HWND with the existing BranchWatch Phase 0 diagnostic and Task View.
- Repeat on two virtual desktops, mixed DPI, portrait and negative coordinates, and with a foreground change. Record browser version, Windows build, observations and failures. Test maximized, minimized and private windows, missing registration and incorrect add-on ID.
- Test existing Chrome and Edge capture and restore to ensure their parent-window path is unchanged.
- Run the two-marker test in each of two Firefox windows with identical bounds, in separate profiles and desktops, at mixed DPI, with focus changes, interrupted navigation and a second diagnostic tab. Verify the returned HWND against the Phase 0 probe and confirm marker cleanup. Record failures and any interference from title-customizing extensions.
- The two-marker implementation is a candidate correlation mechanism, not a qualified general mapping. Until real Windows evidence exists and capture/restore authorization is designed separately, do not enable Firefox URL persistence, profile-aware reopening or automatic capture.

The native response can contain HWND, process ID, desktop GUID and geometry; review before sharing. No reports are automatically saved.

## Cleanup

```powershell
pwsh -File scripts/firefox-companion.ps1 -Action Unregister
```

Remove the temporary add-on in about:debugging. Delete the local diagnostic host directory if desired. Unregistration refuses to remove a registration pointing to a different manifest.

References: [Mozilla native messaging](https://developer.mozilla.org/en-US/docs/Mozilla/Add-ons/WebExtensions/Native_messaging), [native manifests](https://developer.mozilla.org/en-US/docs/Mozilla/Add-ons/WebExtensions/Native_manifests), [temporary extension installation](https://extensionworkshop.com/documentation/develop/temporary-installation-in-firefox/).

Phase 8G.3A prepares [offline Firefox session data and live-operation gates](desktop-memory-firefox-session-preparation.md). Real browser capture and reopening remain disabled.

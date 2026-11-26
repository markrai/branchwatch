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

The response is always explicitly diagnostic-only. Even an exact geometry match at 100% scaling cannot establish association. A popup may steal focus, causing a rejected native candidate; that is an expected observation to record, not a reason to relax the checks. The diagnostic uses neither tab permission nor content scripts, does not change native window state or browser titles, and does not write snapshots or transmit data remotely. It does not expose capture or restore operations. Chromium retains its existing parent-handle checks and restore protocol v2.

## Physical qualification

- Check the hello operation in Firefox with BranchWatch running and closed. Verify separate profile identifiers in independently installed profiles.
- Open two normal Firefox windows with different bounds, then identical bounds, then separate profiles. Inspect each popup and compare candidate HWND with the existing BranchWatch Phase 0 diagnostic and Task View.
- Repeat on two virtual desktops, mixed DPI, portrait and negative coordinates, and with a foreground change. Record browser version, Windows build, observations and failures. Test maximized, minimized and private windows, missing registration and incorrect add-on ID.
- Test existing Chrome and Edge capture and restore to ensure their parent-window path is unchanged.
- Do not treat any result as a proven browser-window mapping. A positive proof mechanism needs separate design and qualification, such as a scoped extension-owned challenge with safe cleanup, or a supported native browser integration. Avoid title guessing and profile-path heuristics. Until such a mechanism is qualified, do not enable Firefox URL persistence, profile-aware reopening or automatic capture. Those remain unfinished portions of Phase 8G.

The native response can contain HWND, process ID, desktop GUID and geometry; review before sharing. No reports are automatically saved.

## Cleanup

```powershell
pwsh -File scripts/firefox-companion.ps1 -Action Unregister
```

Remove the temporary add-on in about:debugging. Delete the local diagnostic host directory if desired. Unregistration refuses to remove a registration pointing to a different manifest.

References: [Mozilla native messaging](https://developer.mozilla.org/en-US/docs/Mozilla/Add-ons/WebExtensions/Native_messaging), [native manifests](https://developer.mozilla.org/en-US/docs/Mozilla/Add-ons/WebExtensions/Native_manifests), [temporary extension installation](https://extensionworkshop.com/documentation/develop/temporary-installation-in-firefox/).

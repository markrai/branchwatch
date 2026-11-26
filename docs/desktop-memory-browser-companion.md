# Desktop Memory browser integration: Phase 8B

This phase establishes a development-only Chrome/Edge companion and a local handshake with the running BranchWatch application. It does not collect tabs or URLs, store browser sessions, or restore browser windows. The separate Phase 8A popup diagnostic remains available.

## Try the connection

Use PowerShell 7 and .NET 10 on Windows. Build and run this version of BranchWatch (exit the installed instance first). In chrome://extensions or edge://extensions enable developer mode, choose Load unpacked, and select `tools/browser-companion-extension`. Copy the actual extension ID. Register the native host explicitly:

```powershell
pwsh -File scripts/browser-companion.ps1 -Action Register -Browser Chrome -ExtensionId YOUR_EXTENSION_ID
```

Repeat with Edge and its own extension ID. Click the Companion toolbar icon and Check connection. The popup should report Connected and display an opaque profile ID. The same profile keeps its random ID across browser restarts; different independently installed profiles get different IDs. Clearing extension storage, removing the extension or copying profile data can change or duplicate the ID. It is not a browser account identifier or authentication credential.

## Architecture and boundaries

The popup requests a one-shot hello through its own MV3 service worker. The worker calls the native messaging host, which validates the registered extension origin and relays a strictly bounded versioned hello to a separate same-user, session-specific Windows named pipe. The tray listener replies with the nonce and profile ID. No HWND is required for this hello; a service worker's zero parent handle does not establish window correlation. No open TCP port, continuous polling, background history collection, tab permission or network requests. No profile inventory is retained in the tray, and no changes are made to snapshots. The endpoint is present while BranchWatch runs, but has only a read-only hello operation. Other processes running under the same Windows user can potentially impersonate the development endpoint or client; this is not authorization to read or modify browser sessions. Future capture/restore must add an explicit consent and trusted correlation gate. Browser native messaging registration is allowlisted to the specified extension ID, not a store-published identity.

The protocol rejects unknown fields, duplicated fields, unsupported versions and commands, malformed IDs, oversized messages and truncated frames. Requests and connection attempts are bounded in size and time. Malformed clients cannot permanently occupy the listener. The host is built and registered separately, not automatically installed or distributed with the production package.

## Qualification

- Check Chrome and Edge separately, restart browser and BranchWatch, confirm stable profile IDs and unavailable status when BranchWatch is closed.
- Install in two independent profiles and verify different IDs; check simultaneous requests and separate Windows sessions.
- Try wrong extension ID and missing registration; verify no connection is reported.
- Confirm that the service worker can connect without a nonzero parent HWND. Do not infer any window association from this result.
- Verify no tab or host permissions, no private-mode access, no URL files, no network connection and no snapshot changes.
- Unregister with `pwsh -File scripts/browser-companion.ps1 -Action Unregister -Browser Chrome` (or Edge). Remove the unpacked extension in the browser UI; local executable and manifest remain in LOCALAPPDATA and can be removed manually.

Phase 8A physical correlation remains outstanding. Phase 8C must not attach URLs to desktop snapshots until the browser-to-native mapping is qualified. No browser session restoration is enabled by this handshake.

Phase 8C adds [explicit experimental per-window capture](desktop-memory-browser-capture.md). The hello operation remains unchanged; tab access requires a separate optional permission and a one-time capture code.

Phase 8E adds an explicit restore operation. See [browser reopening](desktop-memory-browser-restore.md); it requires an updated native host and extension.

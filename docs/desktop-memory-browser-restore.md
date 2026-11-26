# Desktop Memory: Phase 8E profile-aware browser reopening

Experimental, manual per-window workflow. Requires a saved browser context from Phase 8C/D with at least one selected URL. This is not full automatic desktop restoration.

## Try it

1. Exit the installed BranchWatch instance and build/run this version. Reload the unpacked Chrome or Edge Companion extension (0.4.1). Unregister and re-register the matching browser native host using scripts/browser-companion.ps1 and the same extension ID, so the binary supports restore.
2. Switch to the original saved virtual desktop. Open Settings → Desktop Memory and select the saved snapshot (discard any unsaved capture). Review the URLs and profile identifier. Click Reopen saved tabs for the chosen browser window and confirm.
3. Manually open a normal, non-private window of the matching browser profile on that desktop. Open Companion, paste the two-minute restore code, and click Reopen saved tabs. Grant optional tabs permission if needed. If the permission prompt closes the popup, reopen it and paste again before expiry.
4. The native host verifies the parent window and active desktop before and after requesting the approved URLs. BranchWatch checks the saved snapshot again, browser family, opaque profile ID, desktop and one-time code. The extension rechecks the source window and performs the [Phase 8F duplicate check](desktop-memory-browser-reconciliation.md) before asking the browser to open a new normal window. It verifies the new window's initial tab IDs and URLs and applies pinned and active state; the first retained tab becomes active if no saved tab was selected. The new window's native HWND, placement and final virtual desktop are not verified. Arrange it manually.
5. Inspect the new window. Companion reports opened, already present, overlap, inventory unverified, rejected, partial or uncertain; the last result is available when reopening its popup. BranchWatch reports only that a request was issued, not that a window was restored. Do not retry blindly if the result is uncertain.

## Boundaries

No launch of an absent browser, no profile switching, no closing/reusing existing tabs, no automatic native window move, no private session, no empty-window fallback. Only supported HTTP(S) URLs are sent, with full query strings and fragments. A saved tab list must be nonempty, within size limits and have a valid pinned-prefix order. Duplicate URLs are preserved deliberately within the saved list.

One approval can be outstanding and expires after two minutes. Cancelling, changing desktop selection, starting capture, deleting/reloading a snapshot, or closing/hiding the settings page cancels unused approval. Redemption consumes the request before returning URLs, even if the host disconnects or the browser fails. A window can be issued only once per BranchWatch run for a specific snapshot capture; restarting or replacing a snapshot clears that protection. Phase 8F checks existing normal windows in the profile before creating anything. It does not merge partial sessions or verify other windows' native desktop membership. Check the Companion result before restarting to retry.

The profile ID is extension-local and can be duplicated by copying profile data; it is not a browser account identity or authentication credential. Same-user processes remain inside the trust boundary. The new-window operation is not atomic with OS desktop verification; a desktop switch after authorization can place the new window elsewhere. Native window correlation, placement and interactive browser behavior remain physically unqualified. Do not treat this development flow as production-ready. No URL payload is stored in the extension; only a session-local outcome and bounded nonce replay ledger. Browser history may naturally record navigated URLs. No telemetry or cloud relay. The existing plaintext snapshots and recovery backups retain URLs.

## Qualification

Test a disposable normal window in Chrome and Edge, two profiles, two virtual desktops, duplicate URLs, pinned prefix, active fallback, zero selected tabs, wrong browser/profile/desktop/code, expired and replayed code, changed/deleted snapshot, denied tab permission, host mismatch, partial browser operation and desktop switching during reopen. Check that no pre-existing tab is modified, that a second attempt is blocked, and that ordinary Desktop Memory restore still skips browser-context windows. Test mixed DPI manually; native placement is deliberately not attempted. Automatic capture, window placement, and physical reconciliation qualification remain outstanding.

Phase 8H.1 strengthens the initial, pre-update and final browser tab identity and URL checks. A redirect or changed pending destination is reported as partial when observed; a matching pending URL is evidence of an intended navigation, not proof of successful page load. Reload the current Companion extension (0.4.1). No protocol or native host change in this slice.

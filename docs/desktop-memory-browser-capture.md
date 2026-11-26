# Desktop Memory browser integration: Phase 8C

Experimental, explicitly initiated per-window capture. This phase does not establish unattended browser-window inventory or authorize browser restoration. Physical parent-HWND correlation from Phase 8A remains outstanding.

## Try it

1. Build and run the current BranchWatch checkout. Reload the unpacked Companion extension in Chrome or Edge. If the Phase 8B native host is registered, unregister then register again with the same extension ID using scripts/browser-companion.ps1 so the host binary understands capture requests.
2. Open Settings → Desktop Memory on the intended active virtual desktop. Capture the layout. Browser tabs appears when a supported normal Chrome/Edge window can be correlated to the native capture. Select that exact window and Generate capture code. Copy the displayed code.
3. In the selected browser window (not a different profile or window), open the Companion toolbar popup. Paste the code and click Send this window's tabs. Grant optional tabs permission when prompted. If the permission prompt closes the popup, reopen and paste again. No background collection takes place.
4. Return to BranchWatch, click Import received tabs, confirm the count and review the URLs. Select individual tabs or clear/select all tabs for a window. Unchecked URLs are excluded on save. Repeat for another window if needed.
5. Explicitly Save snapshot (or Replace snapshot) to persist the reviewed data. Discarding the capture does not write browser tabs to disk.

The code is random, single-use, valid for two minutes and scoped to the selected captured window. Changing desktop selection, closing/hiding the page, replacing the draft or saving/discarding cancels the outstanding code. Received tabs expire from the in-memory inbox. The native host observes the popup parent HWND, executable family, process creation time and desktop membership; the app compares these with transient captured identity and rechecks live membership before attaching the tabs. The browser popup verifies its focused normal non-private window and stable tab observations. No title-based URL mapping is used. The manual selection and explicit review are necessary until the mapping is physically qualified. A zero or incorrect parent fails closed. Same-user IPC is not an authentication boundary against another process running as the same Windows user.

## Saved information and privacy

Per browser window: browser family, random profile ID, ordered HTTP(S) URLs, pinned and active flags. Duplicate URLs are retained. Private windows, internal pages, file URLs, credential-bearing URLs, pending navigations and unsupported URLs are excluded; a capture is refused if it has no eligible tabs or more than 100 eligible tabs, or exceeds message limits. The extension does not request history, cookies or content-script permissions. Optional tabs permission may remain granted; revoke it in the browser extension settings if desired. No background reads are implemented.

Full URL query strings and fragments are retained and may contain secrets. Review before saving. No page titles, cookies, credentials, tab groups, browser account names or transient HWNDs/PIDs are persisted by the tab model. URLs are saved in the existing local plain-text snapshots.json and recovery backup. Do not share those files. Deleting a snapshot is not secure erasure of the backup. No upload or telemetry. Existing snapshots without BrowserContext remain readable. Older versions may discard the new optional property if they rewrite snapshots; do not downgrade and edit snapshots with browser data.

Browser windows with saved tab context are excluded from the normal restore engine until the profile-aware browser restoration adapter exists. They will not launch empty browser instances as a substitute for restoring tabs.

## Qualification

Test Chrome and Edge on two desktops with multiple windows and profiles. Verify correct window capture and ordered URLs, pinned/active flags, duplicate URLs, wrong-window rejection, expired/used code, denied permission, private/unsupported URLs, moving between desktops, closing the browser before import, discarding a draft, saving/reloading, and backup privacy. Test mixed DPI and portrait displays. Record any parent-window mapping failures. No successful physical run is claimed by this PR.

Phase 8D adds per-window tab review and selection. See [browser tab review](desktop-memory-browser-review.md). Next: profile-aware restoration (8E), reconciliation/duplicate prevention (8F). Automatic all-window capture still requires a qualified background mapping strategy.

Phase 8E adds [explicit Companion reopening](desktop-memory-browser-restore.md) for saved, nonempty browser sessions. Ordinary native window restoration remains blocked for these sessions.

# Desktop Memory: Phase 3

Select a saved snapshot in Settings → Desktop Memory, switch to its original virtual desktop, choose Preview restoration, review the monitor mappings and proposed bounds, and select Restore existing windows. BranchWatch checks the current running inventory, shows per-window matching results and asks for confirmation before issuing placement requests.

Only normal windows with a unique exact title, class and executable path match are eligible. Duplicate identities, missing paths/titles, truncated identities, unverified desktop membership, owned/tool windows, cloaked windows and minimized/maximized windows are skipped. The selected desktop must be active. A title can change even when an application is the same; this is intentionally not fuzzy matching. Documented Windows membership APIs do not prove exclusive membership for every pinned window, so pinned-window behavior remains a hardware qualification item.

The planner is recalculated against a fresh monitor topology and compared with the reviewed plan. Before each move, running windows are re-enumerated and the approved transient HWND, PID, thread ID and process creation time must still match. Immediately before placement, native membership, title, class, path, normal state and geometry are checked again. There is no atomic Windows API combining these checks and the mutation; an external change in the final interval cannot be entirely excluded.

Only SetWindowPos with no activation, no z-order or owner z-order change, and asynchronous cross-thread positioning is used. Requests are verified against the actual screen rectangle for a bounded interval. An application may constrain its own bounds; an unverified request is reported as such, not as success, and stops further moves. A queued request may finish later. Cancel stops subsequent requests, not already issued ones. Display/desktop drift stops remaining moves. No automatic rollback, application launch, termination, state changes, cross-desktop movement or display configuration changes occur. No snapshot is rewritten.

## Manual qualification

- Save a desktop with two differently titled normal windows from the same application. Move both, preview and restore. Verify position/size and that unrelated windows on this and another desktop remain untouched.
- Repeat restore without changing the layout. Results should be AlreadyPlaced and no duplicate windows should appear.
- Try identical titles, changed titles, a closed application, a minimized/maximized window, a pinned window, and a window running at higher integrity. Verify conservative skips or explicit failure; no launches.
- Change display configuration after preview and verify restore refuses the stale plan. Refresh and try a missing monitor with skip then explicitly selected primary fallback.
- Switch desktops during execution or cancel. Confirm no subsequent requests are issued and partial results are visible.
- Validate mixed DPI and portrait displays, spanning windows, taskbar offsets, and application-enforced minimum sizes. Check that no foreground activation or z-order changes are intentionally requested.

Physical validation is required before broad use. Phase 4 will add launching missing applications; non-normal show-state restoration is deliberately deferred until native placement evidence is available.

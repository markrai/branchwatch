# Desktop Memory: Phase 4

Select a saved snapshot on its original active desktop and preview restoration. Enable **Launch eligible missing applications** if desired, then choose Restore desktop. The checkbox defaults off and resets when the preview is discarded. Existing-window matching is unchanged. The confirmation lists eligible executable paths before any application starts.

Generic launch is deliberately limited:

- One saved normal window per executable; duplicate saved identities and non-normal or unplaceable windows are not launched.
- Canonical absolute .exe paths on fixed local drives only. Missing executables, aliases, reparse points, and paths below reparse-point directories are blocked. No shell, arguments, document paths, elevation, environment overrides, or command line recovery. Working directory is the executable directory.
- A visible window using the executable on any desktop, or any running process with the same executable name, blocks launching. This may exclude apps that use background processes. In-memory session guard blocks repeat attempts even after failure, cancellation or timeout. Restarting BranchWatch resets the guard; inspect the running app before retrying.
- Before each start, recheck the approved snapshot's desktop and display topology and absence of the application. There is no atomic Windows check-and-start API; concurrent external launches and desktop changes cannot be entirely excluded.
- The process returned by the direct launch must still be running. Match its PID and creation time, a previously unseen window handle, exact saved title/class/path, verified desktop membership, and normal state. Require the same unique token in consecutive observations; recheck immediately before placement. No child-process or existing-instance delegation is inferred.
- Poll at most 40 times, with a 250 ms wait between observations. Inventory time adds to the nominal ten-second window. Cancellation stops future requests but does not kill a process or undo an issued placement.
- A start without a verified window is reported as LaunchUnverified and remaining launches stop. A successful launch alone is not a successful restore. Application-driven foreground activation is possible. No application is killed, no unrelated window is deliberately moved, and no snapshot is rewritten.

Generic launching does not restore projects, browser tabs, document contents, terminal working directories or arbitrary application state. Apps whose default window title differs from the saved title may start but remain unpositioned. Phase 5 will add application-specific adapters.

## Manual qualification

Use a harmless standalone desktop application with a stable title. Save its window, close it, preview the saved layout, and restore with launch disabled (no start), then enabled (review exact executable path). Verify the new window belongs to the direct process on the original desktop and has the expected bounds. Restore twice and verify no extra instances. Try an app already running on another desktop, two saved windows from one executable, missing executable, a single-instance/delegating app, timeout, cancellation, and monitor or desktop changes. Verify blocked/unverified outcomes are explicit and no automatic retry occurs.

Native execution remains unqualified until physical Windows tests are completed.

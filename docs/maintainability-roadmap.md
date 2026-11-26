# BranchWatch maintainability roadmap

Source baseline: d0c542e10debdc212123824f0c7c4588fa49190d. Preserve guarded native operations, one-shot alarm behavior, snapshot formats, user-visible workflows and fail-closed browser gates. Prefer narrow changes, explicit state ownership and testable services to a framework rewrite.

| Phase | Scope |
| --- | --- |
| R.0 | Lifecycle and persistence fixes, clean CI warning baseline |
| R.1 | Sticky-note interaction coordination |
| R.2 | Passive body and native-window lifecycle |
| R.3 | Desktop Memory capture and draft management |
| R.4 | Restoration orchestration and cancellation |
| R.5 | Browser authorization and correlation policies |
| R.6 | Tray and settings coordination |
| R.7 | Refresh paths and test infrastructure |
| R.8 | Architectural review and regression qualification |

## R.0

The sticky-note controller previously saved before closing windows, although closing can commit an unfinished title edit. Capture pending titles, geometry and due alarm state before closing, suppress ordinary close-state transitions during application disposal, then perform the final save after close callbacks. Retain the established session-ending disposal path: WPF can close windows before OnExit, so deferring disposal would risk ordinary close callbacks marking notes closed during shutdown. A shutdown cancelled externally after the notification remains an unqualified lifecycle scenario, not a resolved defect. Start is idempotent and cannot revive a disposed controller. A minimal native desktop interface permits exercising the actual controller and WPF windows without requiring virtual desktop COM support in CI.

Tests cover unfinished rename on shutdown, cancelled rename and dismissed alarm, ordinary close versus shutdown, and duplicate start/disposal. Windows CI rejects warnings in the solution test build. Existing browser, packaging and evidence checks remain unchanged. Physical session-ending/cancellation, desktop affinity, focus and native click-through still require real Windows acceptance. Disk-write failure handling remains best-effort at exit; no guaranteed persistence is claimed when storage is unavailable.

Later phases should preserve the current user-facing behavior and not unlock unqualified Firefox operations or native browser placement.

## R.1

StickyNoteInteraction owns temporary editing transitions, the rename baseline, alarm dialog reentry and closing guards. It delegates the existing five-second preview state to StickyNoteTemporaryEditing and operates on the existing per-note model. Header acknowledgement takes precedence over temporary editing, including for collapsed notes. All time-dependent decisions take explicit timestamps. The WPF window translates the returned transitions into focus, timer, hook and presentation effects; it no longer owns a second copy of these interaction flags or the rename comparison. No new serialized state is introduced. While a rename or alarm dialog is active, new temporary edit sessions are rejected, and new rename/dialog actions are rejected during closing. Closing still permits committing an already-started rename. Existing window geometry, passive-body/native ownership, modal dialog implementation, controller persistence and alarm scheduling remain unchanged. R.2 owns native lifecycle extraction.

Pure interaction regression tests cover the preview deadline, held editing, alarm priority, collapse/opaque guards, rename commit/cancel/reset, dialog reentry and closing. Existing STA tests retain their window-level assertions; physical focus, mouse, native click-through and timing remain unqualified.

The alarm dialog now explicitly commits an active title edit before showing, instead of relying solely on the subsequent WPF deactivation event. This is a defensive ordering change preserving the established focus-loss commit semantics. Six pure tests and one WPF combined alarm/editing regression were added. No line-count target: the window still owns native presentation effects, which are the subject of R.2.

## R.2

StickyNotePassiveBodyLifecycle owns creation, attachment, alignment, reentry, failure suppression, removal and disposal of the auxiliary click-through body. The window supplies eligibility and layout readiness, handles editing on desktop changes, and renders the owner when attachment changes. A small disposable body interface allows lifecycle testing without Win32. The existing native adapter still owns the HWND, WPF body, styles, pixel bounds and alarm animation. Native ownership and current-desktop checks are retained, including a fresh affinity check before changed native placement. A failed show or alignment leaves the owner presentation in place and suppresses repeated creation until an explicit transparency change or expansion resets the failure. An affinity loss observed during refresh or failed alignment detaches the body without latching failure; the next eligible refresh can recreate it. A failed candidate is disposed before failure notification. Closing is terminal and does not render the closing owner. A candidate invalidated by a reentrant removal or shutdown cannot attach later. No persistent data, settings, alarm deadlines, transparency levels or native style flags change.

Eight pure lifecycle regression tests cover verified owner/layout gating, reuse, show failure, alignment failure, affinity/eligibility loss, reentrant removal, close during creation, exception cleanup and idempotent disposal. Existing WPF interaction and persistence regressions remain. The window code-behind is approximately 717 lines (802 before R.2). The new tests use fake bodies; real HWND styles, multi-monitor alignment, mixed DPI, desktop switches, collapse/expand and alarm outlines still require physical Windows testing. This slice does not replace the native desktop manager or outside-click hook; the hook remains owned by the UI effect dispatcher.

## R.3

DesktopMemoryCaptureService owns capture/probe and transient browser token correlation, plus the existing native re-verification predicate before import. DesktopMemoryDraft owns the unsaved snapshot, transient tokens and browser tab review; folder updates and browser imports require the exact expected draft reference. It validates draft changes and saves the reviewed projection through the existing encrypted store, clearing the draft only after successful persistence. Browser selections synchronize on model changes, not WPF rendering. The shared DesktopMemorySta helper eliminates duplicate STA thread startup logic. The page retains selection/visibility checks, user confirmations, status text, ticket ownership, and restore orchestration for R.4/R.5. No new permission, schema, or browser family is enabled. Stopping the page clears transient draft state; hiding the page retains the draft as before. A stale folder dialog cannot edit a replaced draft. Native and browser qualification remains required.

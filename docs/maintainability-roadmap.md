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

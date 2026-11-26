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

The sticky-note controller previously saved before closing windows, although closing can commit an unfinished title edit. Capture pending titles, geometry and due alarm state before closing, suppress ordinary close-state transitions during application disposal, then perform the final save after close callbacks. Windows session-ending notifications flush pending changes without disposing windows or stopping timers; a cancelled shutdown can therefore leave the app usable. Start is idempotent and cannot revive a disposed controller. A minimal native desktop interface permits exercising the actual controller and WPF windows without requiring virtual desktop COM support in CI.

Tests cover unfinished rename on shutdown, flush followed by further editing, ordinary close versus shutdown, and duplicate start/disposal. Windows CI rejects warnings in the solution test build. Existing browser, packaging and evidence checks remain unchanged. Physical session-ending/cancellation, desktop affinity, focus and native click-through still require real Windows acceptance. Disk-write failure handling remains best-effort at exit; no guaranteed persistence is claimed when storage is unavailable.

Later phases should preserve the current user-facing behavior and not unlock unqualified Firefox operations or native browser placement.

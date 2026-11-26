# Desktop Memory: Phase 8D browser tab review

After importing tabs into an unsaved capture, review the Browser sessions section. Each window is shown independently, in captured order, with its browser family, monitor source and shortened opaque profile identifier. Every tab displays its original order, full URL, pinned and active flags, and a review hint if the URL contains a query or fragment. That hint is not a secret detector.

All imported tabs start checked. Uncheck individual tabs or use Select all tabs / Clear selection for that window. Duplicate URLs remain separate by position. Selection survives rerendering, switching the selected desktop and importing other browser windows in the same draft. Recapturing a window replaces its context and starts a fresh selection; recapturing the layout or discarding it clears the review. A changed capture target cancels an outstanding capture code.

Nothing changes in the live browser. Saving writes only checked URLs in original order, preserving pinned and active flags. If the original active tab is excluded, no retained tab is marked active; restoration must choose its own fallback in a later phase. Excluding all tabs persists a browser context with an empty tab list, not a generic browser launch. This keeps the existing restore block for that browser. Saved snapshots are read-only in this review; capture again to revise them. The recovery backup can retain URLs from previous saves, so deleting or deselecting is not secure erasure of prior data.

Manual checks: capture two windows (including repeated URLs and different profiles), deselect one duplicate, switch desktops and refresh, import a second window, select/clear all, save/reload, and verify exact selected URL order and metadata in the local snapshot. Discard and recapture to verify selection reset. Test a long URL and keyboard navigation in a narrow Settings window. Confirm zero-tab browser sessions are not restored by the ordinary window engine.

No protocol version, native host or extension update is needed for this UI phase. Automatic capture and browser restore remain gated; live Windows qualification is outstanding.

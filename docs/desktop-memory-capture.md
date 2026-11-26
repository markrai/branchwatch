# Desktop Memory: Phase 1

The capture-only settings page is available under Settings → Desktop Memory. Choose the active desktop, capture the current arrangement, review the diagram and inventory, then explicitly save. Saved snapshots are keyed by desktop GUID and remain inspectable even if their original desktop is unavailable. Replacement and deletion require confirmation. An empty capture cannot replace a snapshot.

Capture is deliberately restricted to the active desktop until inactive-desktop enumeration is physically qualified. The active registry hint must resolve without the desktop-1 fallback, must remain stable at both endpoints, and each captured window must have a matching GUID and affirmative current-desktop COM result. These are bounded observations, not an atomic OS snapshot or proof of exclusive membership for pinned windows. Do not switch desktops or rearrange displays during capture. A failed or unstable capture cannot be saved. Windows with incomplete membership or placement are omitted, with counts and diagnostic warnings. Shell and BranchWatch windows are excluded.

## Local data

`%APPDATA%\BranchWatch\desktop-memory\snapshots.json` holds versioned, plain-text snapshots. The last valid previous file is retained as `snapshots.backup.json` after replacement or deletion. Deleting a snapshot in the UI is not secure erasure; remove the recovery backup manually if necessary. A malformed or unsupported file is not overwritten. The files can contain sensitive window titles, application paths and display identifiers. No screenshots, command lines, environment variables, continuous tracking, network upload, or automatic launch occur.

Captured coordinates distinguish screen bounds, DWM visible bounds and raw WINDOWPLACEMENT normal bounds. The diagram is a schematic, not a screen image; minimized windows are listed but not drawn. Monitor interface candidates may be ambiguous. No HWNDs or process IDs are persisted. Capture is not a guarantee that an application can be relaunched, moved or matched after restart.

## Manual checks

- Capture two desktops, one at a time, with multiple windows and mixed monitor orientation. Inspect the window list and schematic; confirm that other-desktop and BranchWatch windows are absent.
- Capture again, discard, then confirm the old saved snapshot is unchanged. Capture again and explicitly replace. Restart BranchWatch and inspect both snapshots.
- Select a nonactive desktop and confirm capture is disabled. Switch to that desktop and retry. Check minimized, maximized and spanning windows.
- Corrupt a test copy of the snapshot file and verify the page reports the error without overwriting it. Back up real data before testing.
- Complete the Phase 0 hardware qualification before trusting placement coordinates. See the [existing-window restoration qualification](desktop-memory-restore.md) before using restoration.

Phase 2 adds [monitor matching and a placement preview](desktop-memory-placement.md), without moving windows.

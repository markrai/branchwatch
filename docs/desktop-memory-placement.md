# Desktop Memory: Phase 2

Open Settings → Desktop Memory, select a saved snapshot and click Preview restoration. BranchWatch reads the current monitor configuration twice under a per-monitor DPI context, then calculates proposed placement without reading or changing live application windows. Refresh after changing displays. The preview is not persisted and cannot authorize future execution.

## Policy

- Physical monitor matching requires a unique active interface identity on both sides. Source names, display numbers, primary status, size and friendly names are not identity evidence. Duplicate/clone or missing identity is reported as ambiguous or unverified.
- Exact geometry, known equal rotation/scale and compatible work areas preserve original screen bounds or translate them with the same physical monitor. Where a valid visible frame is available, invisible outer resize borders may extend beyond the usable area.
- Changed resolution, rotation, scaling or work area uses proportional physical-pixel position and size relative to the saved and current work areas, clamped to the target work area. Windows remain upright; DPI is not applied a second time. Apps may enforce minimum sizes later.
- Spanning windows are preserved only if all intersected monitors match and share the same translation with unchanged geometry, and the visible rectangle is fully covered by their work areas. Gaps or changed topology trigger a single-anchor-monitor fit, flagged for review.
- Unmatched anchors default to skipped. Explicitly selecting primary fallback previews those windows on the primary monitor. Overlaps are allowed; this is not a window tiling engine. New displays are not selected automatically.
- Minimized/maximized windows retain their saved state as intent, not a command. Raw normal placement is workspace-relative for ordinary windows but can be screen-relative for tool windows. Phase 1 did not retain enough evidence to unconditionally convert this rectangle. The preview uses a monitor work-origin offset as an estimate only, flags it for review, and skips estimates that do not intersect the saved anchor. Native conversion and show-state execution are not qualified.
- The plan does not modify the snapshot or Windows display settings. No new sensitive data is persisted.

## Qualification

Compare an unchanged layout, a physical monitor moved to the opposite side, changed resolution/scaling/rotation, a missing secondary monitor, and a spanning window crossing a gap. Verify the schematic and per-window positions; choose skip and primary fallback separately. Check that repeated previews produce identical geometry for an unchanged topology. Existing Phase 1 snapshots need no migration.

Phase 3 adds [existing-window restoration](desktop-memory-restore.md). Native placement and minimized/maximized restoration remain subject to physical qualification.

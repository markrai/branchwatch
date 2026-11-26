# Changelog

## [Unreleased]

### Added

- Desktop Memory manual capture, local snapshot storage and inspection for the active virtual desktop. Restoration is not enabled yet.

### Added

- Collapse and expand sticky notes using the underline button, preserving expanded size and state across restarts.
- Unified BranchWatch settings with sidebar navigation, a shared visual style, sticky note color swatches and per-desktop label colors.

### Changed

- Label transparency level 1 as opaque & editable.
- Group tray actions by Git Watcher, Virtual Desktops and Sticky Notes; open the unified settings window at the relevant section.

### Fixed

- Correct the bottom-left resize grip orientation and allow resizing back to the saved original dimensions, including after restarting.
- Align sticky note headers and click-through bodies edge to edge while preserving internal header padding.
- Validate passive sticky note window styles after WPF shows the window, and recognize owned auxiliary windows without a separate virtual desktop ID. Report the failing operation when click-through cannot be enabled.

### Changed

- Translucent sticky note bodies pass pointer input through to underlying applications while the header remains interactive.

### Added

- A discreet bottom-left resize grip for sticky notes, including translucent click-through notes.
- Sticky note preferences for default color and transparency, with an explicit apply-to-all action.

### Fixed

- Sticky note transparency now uses WPF composition instead of modifying native layered-window styles after creation.

## [1.3.0] - 2026-11-25

### Added

- Desktop-specific sticky notes with independent always-on-top windows, colors, adjustable per-note transparency, move and close controls, and local autosave.

## [1.2.2] - 2026-10-03

### Added

- Virtual desktop taskbar overlay neighbor strip: faint labels for desktops to the left and right of the active desktop, anchored so the active name stays at its existing center

### Fixed

- Active virtual desktop name no longer truncates after neighbor-strip layout: `RootBorder` now explicitly owns the freshly calculated active width/height so prior StackPanel arrange state cannot shrink the active label

### Notes

- Neighbor updates reuse the existing registry poll (~300ms). Create/delete/rename/reorder are detected by comparing the full ordered desktop id+name snapshot; there is no COM/event notification integration.

## [1.2.1] - 2026-07-31

### Added

- Virtual desktop overlay `Show only on desktop` toggle so application windows can take z-order precedence
- Virtual desktop overlay `Show on Taskbar` position option, centered and aligned with the taskbar

### Fixed

- Virtual Desktops font color picker not opening or applying correctly
- `Show on Taskbar` overlay disappearing when the taskbar is clicked (uses topmost re-assertion)

### Notes

- Position relative to taskbar HWND (advanced) is not implemented. `Show on Taskbar` does not anchor to the taskbar window handle; it uses work-area geometry and periodic topmost re-assertion instead.

## [1.2.0] - 2026-07-31

### Added

- Separate virtual desktop overlay showing the current Windows virtual desktop name
- Tray menu `Virtual Desktops...` item with a dedicated configuration window for placement and personalization

## [1.1.2] - 2026-06-29

### Fixed

- WorkspaceRepo mode waits for workspace activity on startup when no valid last-active repository is available, instead of auto-selecting the pinned repository

## [1.1.1] - 2026-06-29

### Fixed

- WorkspaceRepo mode restores the last active workspace repository on startup instead of always opening on the pinned repository when both are in the workspace

### Added

- `LastActiveWorkspaceRepositoryPath` setting, persisted when workspace activity changes the active repository

## [1.1.0] - 2026-06-29

### Added

- Optional overlay activity reason display for WorkspaceRepo mode (`Show activity reason` in Personalize...)
- Tray menu `Last activity` item showing the latest workspace activity reason
- `BranchWatch.exe activity "<path>" --reason repo-opened` CLI for explicitly reporting repo focus in WorkspaceRepo mode

### Fixed

- Overlay line order when repository name is shown: branch appears above repository name again

## [1.0.0] - 2026-06-29

Initial release.

### Added

- Windows tray app with an always-on-top overlay showing the active Git repository and branch
- **PinnedRepo** mode: watch a single selected Git repository
- **WorkspaceRepo** mode: watch a parent folder with multiple repositories; the overlay follows the repo with the latest meaningful activity (branch switch, working-tree edit, or index change)
- Workspace discovery with depth limiting and ignore rules for common generated and vendor folders
- Tray menu for repository/workspace selection, overlay appearance, and startup options
- Per-user settings at `%AppData%\BranchWatch\settings.json`
- Optional `Start with Windows` via the current-user Run registry key
- Self-contained publish support for Windows x64

# Changelog

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

# Changelog

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

# Changelog

All notable changes to BranchWatch are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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

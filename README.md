# BranchWatch

**Version 1.0.0**

BranchWatch is a lightweight Windows tray app that displays the active Git repo/branch in a persistent always-on-top overlay.

## Requirements

- Windows 10/11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) for building from source

## Modes

BranchWatch has 2 watch modes.

### PinnedRepo

PinnedRepo mode watches one selected Git repository. This is the default for existing and new users.

Use the tray menu item `Choose repository...` to select a folder inside a Git repository. BranchWatch resolves the repository root, persists it, and shows that repo's current branch.

### WorkspaceRepo

WorkspaceRepo mode watches a parent folder containing multiple Git repositories. BranchWatch recursively discovers repositories under the workspace and monitors both Git metadata and working-tree file activity. The active overlay repo is the repo with the latest meaningful workspace activity.

Use `Choose workspace...` to select the parent folder. Use `Rescan workspace` after adding or removing repositories.

Meaningful activity means a branch switch, a working-tree file edit, or a Git index/staging change. This catches changes from terminals, editors, Cursor, VS Code, and Git clients because BranchWatch watches the workspace filesystem and Git metadata. Editor activity from Cursor or VS Code may promote a repo, which is intentional. Commands such as `git add .` count because they update `.git/index`. BranchWatch intentionally does not track terminal current directories. Merely running `cd` into another repo is out of scope because no files or Git metadata changed.

WorkspaceRepo ignores obvious generated, cache, and vendor folders during discovery and file activity filtering: `.git`, `node_modules`, `bin`, `obj`, `dist`, `build`, `target`, `tmp`, `temp`, `vendor`, `packages`, `.next`, `.nuxt`, `.cache`, and `coverage`.

Workspace discovery is depth-limited to avoid expensive first-run scans. The default `WorkspaceDiscoveryMaxDepth` is `2`, which includes the workspace root, its direct children, and grandchildren. Increase it in `%AppData%\BranchWatch\settings.json` if your repositories are nested more deeply.

## Build

```powershell
dotnet build BranchWatch.sln -c Release
```

## Run from source

```powershell
dotnet run --project BranchWatch
```

An optional path argument still selects and persists a pinned repository:

```powershell
dotnet run --project BranchWatch -- C:\dev\project\scrumboy
```

## Publish

```powershell
dotnet publish BranchWatch/BranchWatch.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The published executable is self-contained and does not require a separate .NET runtime install.

## Settings

Settings are stored per user at:

```text
%AppData%\BranchWatch\settings.json
```

Settings include the pinned repository path, workspace root path, watch mode, workspace discovery depth, internal workspace file activity toggle, overlay visibility, overlay position, opacity, outline, font color, and startup preference.

`Start with Windows` is stored in the current-user registry Run key:

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run\BranchWatch
```
# Desktop Memory: Phase 0 qualification

This read-only diagnostic establishes what Windows exposes before implementing snapshot persistence or restoration. It does not launch, move, resize, activate, close or restore applications, change display settings, or modify BranchWatch preferences. It works before the single-instance gate, so the normal tray application can remain running. Run as the same non-elevated Windows user.

## Run

Build and test from the repository root:

```powershell
dotnet test BranchWatch.sln -c Release
dotnet build BranchWatch/BranchWatch.csproj -c Release
$p = Start-Process -FilePath .\BranchWatch\bin\Release\net10.0-windows\BranchWatch.exe -ArgumentList '--desktop-memory-probe' -Wait -PassThru
$p.ExitCode
explorer "$env:APPDATA\BranchWatch\desktop-memory"
```

Exit 0 means a report was written, 1 means capture/write failed, and 2 means invalid arguments. A failed capture attempts to write a separate .error.json file containing the exception type and HRESULT only; storage failures may prevent even that diagnostic. Every successful run creates a unique desktop-memory-probe-*.json file. A successful exit is not a hardware qualification pass.

By default, window titles, executable paths and desktop names are omitted. To include them locally for distinguishing similar windows, pass '--desktop-memory-probe','--include-identities' to Start-Process. Reports still contain process names, monitor interface paths, desktop GUIDs, window class names, geometry and transient identifiers. Treat all reports as private, inspect and redact before sharing, and never commit them. The ignore rules are a secondary safeguard, not encryption.

## Test matrix

Run once with normal monitor configuration on desktop A with two application windows from the same executable and an Explorer window. Include a maximized window and a minimized window. Switch to desktop B with different windows and run again. Compare known app windows by process name or opt-in title, desktop GUID, IsCurrent, cloak flags and classification.

Repeat after rearranging monitors, changing one display's orientation or scaling, disconnecting/reconnecting a secondary monitor, and restarting Windows. Restore the desired Windows display configuration manually afterward. Keep at least one window spanning two monitors. Include a pinned/all-desktops window if available and record observed membership rather than assuming its GUID represents exclusive membership.

## Interpret the report

- DesktopsBefore/After are registry hints from the existing reader, not authoritative active-desktop proof. That reader can fall back to desktop 1. Compare with per-window COM results and the known desktop selected in Task View.
- MonitorsBefore/After record source names, physical monitor interface candidates, pixel bounds, work areas, rotation, mode and scale factor. Cloned or ambiguous sources are not qualified by a source name alone. Rotation is relative to the device's natural orientation.
- ScreenBounds are GetWindowRect results under a requested per-monitor-v2 DPI context; VisibleFrameBounds are DWM extended frame bounds. NormalPlacementBounds are raw WINDOWPLACEMENT coordinates, which may be workspace-relative. Do not directly pass them to a screen-coordinate positioning API.
- MonitorSource is the nearest monitor reported by Windows (for minimized windows, Windows uses the pre-minimize rectangle); it does not describe every monitor intersected by a spanning window. WindowDpi is the window's reported DPI and not necessarily the physical monitor's effective DPI.
- Windows contains visible top-level HWNDs, including shell-cloaked windows when Windows exposes them. EnumerationIndex is the observed enumeration order, not a promise of restorable z-order. Candidate is a preliminary diagnostic category, not an authorization to move or launch a window. Hidden windows and some app-hosted or pinned windows may not be identifiable.
- DesktopHResult and CurrentHResult preserve raw COM outcomes; CloakHResult, FrameHResult, BoundsError, PlacementError and StyleError preserve other native outcomes. Negative HRESULT means failure. A missing or contradictory desktop identity is not a reason to guess.
- TopologyStable and DesktopHintStable compare the start and end observations. They cannot detect a transient switch away and back. Keep displays and desktops stationary during a run.

## Qualification gate

Before Phase 1, confirm that all expected normal windows are observed, distinct windows are distinguishable, desktop membership agrees with Task View on both desktops, unavailable/ambiguous identities are reported rather than guessed, monitor interface identifiers survive rearrangement and reconnect, and pixel bounds and raw normal placement agree with the known display layout after accounting for workspace offsets. Record failures and Windows build/architecture. If the documented APIs cannot establish membership for inactive desktops, restrict initial capture to the active desktop. Do not implement cross-desktop movement as a workaround.

Phase 0 does not qualify restoration. Future phases require independent verification of monitor matching, coordinate conversion, existing-window placement and application launch correlation.

API references: [IVirtualDesktopManager](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ivirtualdesktopmanager), [EnumDisplayDevicesW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumdisplaydevicesw), [DEVMODEW](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-devmodew), [WINDOWPLACEMENT](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-windowplacement).

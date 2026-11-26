namespace BranchWatch;

internal sealed record SnapshotMonitor(string SourceName, ProbeRect Bounds, ProbeRect WorkArea, bool Primary,
    int? RotationDegrees, int? ModeWidth, int? ModeHeight, int? RefreshHz, int? ScalePercent,
    IReadOnlyList<ProbeDisplayDevice> Devices);

internal sealed record SnapshotWindow(Guid Id, string? ProcessName, string? ExecutablePath, string? Title,
    string? ClassName, ProbeRect ScreenBounds, ProbeRect? VisibleFrameBounds, ProbeRect NormalPlacementBounds,
    bool Minimized, bool Maximized, uint ShowCommand, uint? PlacementFlags, string MonitorSource,
    uint? WindowDpi, int EnumerationIndex, IReadOnlyList<string> IntersectingMonitors)
{
    public ApplicationFolderContext? FolderContext { get; init; }
    public BrowserWindowContext? BrowserContext { get; init; }
}

internal sealed record DesktopSnapshot(Guid DesktopId, string DesktopName, DateTimeOffset CapturedUtc,
    IReadOnlyList<SnapshotMonitor> Monitors, IReadOnlyList<SnapshotWindow> Windows,
    int ExcludedWindowCount, IReadOnlyList<string> Warnings);

internal sealed record DesktopSnapshotFile(int SchemaVersion, IReadOnlyList<DesktopSnapshot> Snapshots);

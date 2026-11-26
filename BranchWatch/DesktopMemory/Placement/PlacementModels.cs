namespace BranchWatch;

internal sealed record CurrentDisplayTopology(DateTimeOffset ObservedUtc,
    IReadOnlyList<SnapshotMonitor> Monitors, IReadOnlyList<string> Warnings);

internal enum UnmatchedMonitorPolicy
{
    Skip,
    Primary
}

internal enum MonitorMatchKind
{
    PhysicalIdentity,
    Missing,
    Ambiguous,
    IdentityUnavailable
}

internal sealed record MonitorMatch(string SavedSource, string? CurrentSource, MonitorMatchKind Kind);

internal enum WindowPlacementKind
{
    Exact,
    Translated,
    Adapted,
    PrimaryFallback,
    Skipped
}

internal enum PlannedWindowState
{
    Normal,
    Minimized,
    Maximized
}

internal sealed record PlannedWindowPlacement(Guid WindowId, string? TargetSource, ProbeRect? ScreenBounds,
    PlannedWindowState State, WindowPlacementKind Kind, bool RequiresReview, string Reason);

internal sealed record DesktopPlacementPlan(Guid DesktopId, DateTimeOffset SnapshotCapturedUtc,
    CurrentDisplayTopology Current, IReadOnlyList<MonitorMatch> MonitorMatches,
    IReadOnlyList<PlannedWindowPlacement> Windows);

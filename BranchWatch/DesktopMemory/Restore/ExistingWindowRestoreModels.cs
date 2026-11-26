namespace BranchWatch;

internal sealed record RestoreWindowKey(string ExecutablePath, string ClassName, string Title)
{
    internal static RestoreWindowKey? Create(string? path, string? className, string? title) =>
        string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(className) || string.IsNullOrWhiteSpace(title)
        || className.Length >= 255 || title.Length >= 512 ? null : new(path, className, title);

    internal bool Matches(RestoreWindowKey? other) => other is not null
        && StringComparer.OrdinalIgnoreCase.Equals(ExecutablePath, other.ExecutablePath)
        && StringComparer.Ordinal.Equals(ClassName, other.ClassName)
        && StringComparer.Ordinal.Equals(Title, other.Title);
}

internal sealed record RestoreWindowToken(IntPtr Handle, uint ProcessId, uint ThreadId, long CreatedFileTime);
internal sealed record RestoreLiveWindow(ProbeWindow Window, RestoreWindowToken? Token);
internal sealed record ExistingRestoreInventory(Guid ActiveDesktopId, CurrentDisplayTopology Topology,
    IReadOnlyList<RestoreLiveWindow> Windows);

internal enum ExistingRestoreStatus
{
    Ready,
    UnsupportedState,
    UnavailablePlacement,
    MissingIdentity,
    MissingWindow,
    AmbiguousWindow,
    UnverifiedWindow,
    AlreadyPlaced,
    Restored,
    Changed,
    Failed,
    Cancelled,
    NotAttempted,
    LaunchReady,
    LaunchBlocked,
    LaunchUnverified,
    LaunchedAndRestored,
    UserSkipped
}

internal sealed record ExistingRestoreItem(Guid WindowId, string Label, PlannedWindowPlacement Placement,
    RestoreWindowKey? Key, RestoreWindowToken? Token, ExistingRestoreStatus Status, string Detail);

internal sealed record ExistingRestorePreparation(DesktopSnapshot Snapshot, DesktopPlacementPlan Plan,
    UnmatchedMonitorPolicy Policy, IReadOnlyList<ExistingRestoreItem> Items);

internal sealed record ExistingRestoreOutcome(Guid WindowId, string Label, ExistingRestoreStatus Status, string Detail);

internal interface IExistingWindowRestoreEnvironment
{
    ExistingRestoreInventory Inspect();
    ExistingRestoreOutcome Apply(Guid desktopId, CurrentDisplayTopology expectedTopology,
        ExistingRestoreItem item, CancellationToken cancellation);
}

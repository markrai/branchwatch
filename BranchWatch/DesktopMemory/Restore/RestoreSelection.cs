namespace BranchWatch;

internal static class RestoreSelection
{
    internal static bool CanSelect(PlannedWindowPlacement placement) =>
        placement.State == PlannedWindowState.Normal && placement.Kind != WindowPlacementKind.Skipped
        && placement.ScreenBounds is { } bounds && DesktopSnapshotCapture.Valid(bounds)
        && bounds.Width <= int.MaxValue && bounds.Height <= int.MaxValue;

    internal static IReadOnlySet<Guid> Default(DesktopPlacementPlan plan) =>
        plan.Windows.Where(w => CanSelect(w) && !w.RequiresReview).Select(w => w.WindowId).ToHashSet();

    internal static IReadOnlySet<Guid> Retain(DesktopPlacementPlan previous, DesktopPlacementPlan next, IReadOnlySet<Guid> selected)
    {
        var byId = previous.Windows.ToDictionary(w => w.WindowId);
        return next.Windows.Where(w => CanSelect(w) && selected.Contains(w.WindowId)
            && byId.TryGetValue(w.WindowId, out var prior) && prior == w)
            .Select(w => w.WindowId).ToHashSet();
    }

    internal static ExistingRestorePreparation Apply(ExistingRestorePreparation preparation, IReadOnlySet<Guid> selected)
    {
        var allowed = preparation.Plan.Windows.Where(CanSelect).Select(w => w.WindowId).ToHashSet();
        if (selected.Any(id => !allowed.Contains(id)))
            throw new InvalidOperationException("Restore selection contains a window without an eligible placement.");
        return preparation with
        {
            Items = preparation.Items.Select(i => selected.Contains(i.WindowId) || !allowed.Contains(i.WindowId)
                ? i : i with { Status = ExistingRestoreStatus.UserSkipped, Detail = "Not selected for this restoration." }).ToArray()
        };
    }
}

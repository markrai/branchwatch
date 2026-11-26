namespace BranchWatch;

internal static class ExistingWindowRestoreMatcher
{
    internal static IReadOnlyList<ExistingRestoreItem> Match(DesktopSnapshot snapshot, DesktopPlacementPlan plan,
        ExistingRestoreInventory inventory)
    {
        var placements = plan.Windows.ToDictionary(w => w.WindowId);
        var savedKeys = snapshot.Windows.ToDictionary(w => w.Id,
            w => RestoreWindowKey.Create(w.ExecutablePath, w.ClassName, w.Title));
        var live = inventory.Windows.Where(w => w.Window.Classification is not ("branchwatch" or "shell" or "auxiliary" or "hidden")
            && (w.Window.Membership.DesktopId == snapshot.DesktopId || w.Window.Membership.IsCurrent == true
                || w.Window.Membership.DesktopId is null)).ToArray();
        var items = new List<ExistingRestoreItem>();
        foreach (var saved in snapshot.Windows.OrderBy(w => w.EnumerationIndex))
        {
            var placement = placements[saved.Id];
            var key = savedKeys[saved.Id];
            var label = saved.Title ?? saved.ProcessName ?? "Unknown application";
            ExistingRestoreItem Item(ExistingRestoreStatus status, string detail, RestoreWindowToken? token = null) =>
                new(saved.Id, label, placement, key, token, status, detail);

            if (placement.State != PlannedWindowState.Normal)
            {
                items.Add(Item(ExistingRestoreStatus.UnsupportedState, "Saved minimized/maximized state is not qualified for restoration."));
                continue;
            }
            if (placement.Kind == WindowPlacementKind.Skipped || placement.ScreenBounds is not { } bounds
                || !DesktopSnapshotCapture.Valid(bounds) || bounds.Width > int.MaxValue || bounds.Height > int.MaxValue)
            {
                items.Add(Item(ExistingRestoreStatus.UnavailablePlacement, "No executable screen placement."));
                continue;
            }
            if (key is null)
            {
                items.Add(Item(ExistingRestoreStatus.MissingIdentity, "Saved executable path, complete title or window class is unavailable."));
                continue;
            }
            if (savedKeys.Values.Count(k => key.Matches(k)) != 1)
            {
                items.Add(Item(ExistingRestoreStatus.AmbiguousWindow, "Multiple saved windows share the same identity."));
                continue;
            }
            var matches = live.Where(w => key.Matches(RestoreWindowKey.Create(w.Window.ExecutablePath,
                w.Window.ClassName, w.Window.Title))).ToArray();
            if (matches.Length == 0)
            {
                items.Add(Item(ExistingRestoreStatus.MissingWindow, "No matching running window on the target desktop."));
                continue;
            }
            if (matches.Length != 1)
            {
                items.Add(Item(ExistingRestoreStatus.AmbiguousWindow, "Multiple running windows share the same identity."));
                continue;
            }
            var match = matches[0];
            var window = match.Window;
            if (match.Token is null || window.Classification != "candidate" || window.Owned || window.ToolWindow
                || window.CloakFlags != 0 || window.Membership.DesktopId != snapshot.DesktopId
                || window.Membership.IsCurrent != true || window.Membership.DesktopHResult < 0
                || window.Membership.CurrentHResult < 0 || window.ScreenBounds is null)
            {
                items.Add(Item(ExistingRestoreStatus.UnverifiedWindow, "Running window identity, desktop membership or eligibility could not be verified."));
                continue;
            }
            if (window.Minimized || window.Maximized)
            {
                items.Add(Item(ExistingRestoreStatus.UnsupportedState, "Running window is minimized or maximized."));
                continue;
            }

            items.Add(Item(ExistingRestoreStatus.Ready, window.ScreenBounds == bounds
                ? "Matching window is already at the proposed coordinates."
                : "Unique running window matched.", match.Token));
        }
        return items;
    }
}

namespace BranchWatch;

internal sealed class MissingApplicationLaunchService(IExistingWindowRestoreEnvironment windows,
    IMissingApplicationLaunchEnvironment launcher)
{
    private static readonly StringComparer Paths = StringComparer.OrdinalIgnoreCase;
    private const int MaximumPolls = 40;

    internal MissingApplicationLaunchPreparation Prepare(ExistingRestorePreparation existing, CancellationToken cancellation)
    {
        var inventory = windows.Inspect();
        EnsureCurrent(existing, inventory);
        var fresh = ExistingWindowRestoreMatcher.Match(existing.Snapshot, existing.Plan, inventory);
        var items = new List<MissingApplicationLaunchItem>();
        foreach (var item in existing.Items.Where(i => i.Status == ExistingRestoreStatus.MissingWindow))
        {
            cancellation.ThrowIfCancellationRequested();
            var path = item.Key?.ExecutablePath;
            string? reason;
            if (path is null) reason = "No saved executable identity.";
            else if (existing.Snapshot.Windows.Count(w => Paths.Equals(w.ExecutablePath, path)) != 1)
                reason = "More than one saved window uses this executable. An application adapter is required.";
            else if (fresh.Single(i => i.WindowId == item.WindowId).Status != ExistingRestoreStatus.MissingWindow)
                reason = "Running window inventory changed. Refresh the restore preview.";
            else if (HasApplicationWindow(inventory, path))
                reason = "An application window with this executable is already present, including on another desktop.";
            else reason = launcher.CheckLaunchBlock(path);

            items.Add(new(item, path, reason is null ? ExistingRestoreStatus.LaunchReady : ExistingRestoreStatus.LaunchBlocked,
                reason ?? "Launch executable without arguments; reposition only a uniquely correlated exact window."));
        }
        return new(existing, items);
    }

    internal IReadOnlyList<ExistingRestoreOutcome> Execute(MissingApplicationLaunchPreparation approved,
        IReadOnlyList<ExistingRestoreOutcome> existingResults, CancellationToken cancellation)
    {
        var existing = approved.Existing;
        var stopped = existingResults.Any(r => r.Status is ExistingRestoreStatus.Changed or ExistingRestoreStatus.Failed
            or ExistingRestoreStatus.Cancelled or ExistingRestoreStatus.NotAttempted);
        var results = existingResults.ToDictionary(r => r.WindowId);
        foreach (var candidate in approved.Items)
        {
            var item = candidate.RestoreItem;
            ExistingRestoreOutcome Outcome(ExistingRestoreStatus status, string detail) =>
                new(item.WindowId, item.Label, status, detail);
            if (candidate.Status != ExistingRestoreStatus.LaunchReady || candidate.ExecutablePath is not { } path)
            {
                results[item.WindowId] = Outcome(candidate.Status, candidate.Detail);
                continue;
            }
            if (stopped || cancellation.IsCancellationRequested)
            {
                results[item.WindowId] = Outcome(cancellation.IsCancellationRequested ? ExistingRestoreStatus.Cancelled
                    : ExistingRestoreStatus.NotAttempted, "No application launch requested.");
                continue;
            }

            var attempted = false;
            try
            {
                cancellation.ThrowIfCancellationRequested();
                var before = windows.Inspect();
                EnsureCurrent(existing, before);
                var match = ExistingWindowRestoreMatcher.Match(existing.Snapshot, existing.Plan, before)
                    .Single(i => i.WindowId == item.WindowId);
                if (match.Status != ExistingRestoreStatus.MissingWindow || HasApplicationWindow(before, path))
                {
                    results[item.WindowId] = Outcome(ExistingRestoreStatus.LaunchBlocked,
                        "Application or matching window appeared after approval. No launch requested.");
                    continue;
                }
                if (launcher.CheckLaunchBlock(path) is { } reason)
                {
                    results[item.WindowId] = Outcome(ExistingRestoreStatus.LaunchBlocked, reason);
                    continue;
                }

                cancellation.ThrowIfCancellationRequested();
                attempted = true;
                var process = launcher.Launch(path, existing.Snapshot.DesktopId, existing.Plan.Current, cancellation);
                var priorHandles = before.Windows.Select(w => w.Window.Handle).ToHashSet(StringComparer.OrdinalIgnoreCase);
                RestoreWindowToken? observed = null;
                ExistingRestoreItem? correlated = null;
                for (var poll = 0; poll < MaximumPolls; poll++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (!launcher.IsRunning(process)) break;
                    var inventory = windows.Inspect();
                    EnsureCurrent(existing, inventory);
                    var fresh = ExistingWindowRestoreMatcher.Match(existing.Snapshot, existing.Plan, inventory)
                        .Single(i => i.WindowId == item.WindowId);
                    if (fresh.Status == ExistingRestoreStatus.AmbiguousWindow) break;
                    if (fresh.Status == ExistingRestoreStatus.Ready && fresh.Token is { } token
                        && token.ProcessId == process.ProcessId && token.CreatedFileTime == process.CreatedFileTime
                        && inventory.Windows.Any(w => w.Token == token && !priorHandles.Contains(w.Window.Handle)))
                    {
                        if (observed == token)
                        {
                            correlated = fresh;
                            break;
                        }
                        observed = token;
                    }
                    else
                    {
                        observed = null;
                    }
                    if (poll + 1 < MaximumPolls && !launcher.WaitForInventory(cancellation)) break;
                }
                if (correlated is null)
                {
                    stopped = true;
                    results[item.WindowId] = Outcome(ExistingRestoreStatus.LaunchUnverified,
                        "Application was started, but no unique new matching window was verified. It was left running; no retry or unrelated move was attempted.");
                    continue;
                }
                cancellation.ThrowIfCancellationRequested();
                var finalInventory = windows.Inspect();
                EnsureCurrent(existing, finalInventory);
                var finalMatch = ExistingWindowRestoreMatcher.Match(existing.Snapshot, existing.Plan, finalInventory)
                    .Single(i => i.WindowId == item.WindowId);
                if (!launcher.IsRunning(process) || finalMatch.Status != ExistingRestoreStatus.Ready || finalMatch.Token != correlated.Token)
                {
                    stopped = true;
                    results[item.WindowId] = Outcome(ExistingRestoreStatus.LaunchUnverified,
                        "Application started, but correlated window changed before positioning. No move was requested.");
                    continue;
                }
                var placement = windows.Apply(existing.Snapshot.DesktopId, existing.Plan.Current, finalMatch, cancellation);
                if (placement.Status is ExistingRestoreStatus.Restored or ExistingRestoreStatus.AlreadyPlaced)
                    results[item.WindowId] = Outcome(ExistingRestoreStatus.LaunchedAndRestored,
                        "Started application; new matching window bounds verified.");
                else
                {
                    stopped = true;
                    results[item.WindowId] = Outcome(placement.Status, "Application started. " + placement.Detail);
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                stopped = true;
                results[item.WindowId] = Outcome(ExistingRestoreStatus.Cancelled, attempted
                    ? "Cancelled after launch attempt; the application may remain running. No further launch requested."
                    : "Cancelled before launch.");
            }
            catch (Exception ex)
            {
                stopped = true;
                results[item.WindowId] = Outcome(attempted ? ExistingRestoreStatus.LaunchUnverified : ExistingRestoreStatus.Changed,
                    (attempted ? "Launch attempted; outcome unverified. " : "Launch stopped before execution. ") + ex.Message);
            }
        }
        return existing.Items.Select(i => results[i.WindowId]).ToArray();
    }

    private static bool HasApplicationWindow(ExistingRestoreInventory inventory, string path) =>
        inventory.Windows.Any(w => Paths.Equals(w.Window.ExecutablePath, path));

    private static void EnsureCurrent(ExistingRestorePreparation existing, ExistingRestoreInventory inventory)
    {
        if (inventory.ActiveDesktopId != existing.Snapshot.DesktopId
            || !ExistingWindowRestoreService.SameDisplays(existing.Plan.Current, inventory.Topology))
            throw new InvalidOperationException("Desktop or monitor configuration changed. Refresh before continuing.");
    }
}

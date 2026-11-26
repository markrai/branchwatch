namespace BranchWatch;

internal sealed class ExistingWindowRestoreService(IExistingWindowRestoreEnvironment environment)
{
    internal ExistingRestorePreparation Prepare(DesktopSnapshot snapshot, DesktopPlacementPlan reviewed,
        UnmatchedMonitorPolicy policy, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var inventory = environment.Inspect();
        cancellation.ThrowIfCancellationRequested();
        if (inventory.ActiveDesktopId != snapshot.DesktopId)
            throw new InvalidOperationException("Switch to the snapshot's original desktop before restoring.");
        if (reviewed.DesktopId != snapshot.DesktopId || reviewed.SnapshotCapturedUtc != snapshot.CapturedUtc
            || !SameDisplays(reviewed.Current, inventory.Topology))
            throw new InvalidOperationException("The reviewed monitor layout is stale. Refresh the restore preview.");
        var plan = DesktopPlacementPlanner.Plan(snapshot, inventory.Topology, policy);
        if (!plan.Windows.SequenceEqual(reviewed.Windows) || !plan.MonitorMatches.SequenceEqual(reviewed.MonitorMatches))
            throw new InvalidOperationException("The placement plan changed. Refresh the restore preview.");

        return new(snapshot, plan, policy, ExistingWindowRestoreMatcher.Match(snapshot, plan, inventory));
    }

    internal IReadOnlyList<ExistingRestoreOutcome> Execute(ExistingRestorePreparation approved, CancellationToken cancellation,
        Action<ExistingRestoreOutcome>? progress = null)
    {
        var results = new List<ExistingRestoreOutcome>();
        void Record(ExistingRestoreOutcome outcome)
        {
            results.Add(outcome);
            progress?.Invoke(outcome);
        }
        var stopped = false;
        foreach (var item in approved.Items)
        {
            ExistingRestoreOutcome Outcome(ExistingRestoreStatus status, string detail) =>
                new(item.WindowId, item.Label, status, detail);

            if (item.Status != ExistingRestoreStatus.Ready)
            {
                Record(Outcome(item.Status, item.Detail));
                continue;
            }
            if (cancellation.IsCancellationRequested || stopped)
            {
                Record(Outcome(cancellation.IsCancellationRequested ? ExistingRestoreStatus.Cancelled : ExistingRestoreStatus.NotAttempted,
                    "No placement request was issued."));
                continue;
            }

            try
            {
                var inventory = environment.Inspect();
                cancellation.ThrowIfCancellationRequested();
                if (inventory.ActiveDesktopId != approved.Snapshot.DesktopId || !SameDisplays(approved.Plan.Current, inventory.Topology))
                {
                    stopped = true;
                    Record(Outcome(ExistingRestoreStatus.Changed, "Desktop or display configuration changed. Remaining moves stopped."));
                    continue;
                }

                var current = ExistingWindowRestoreMatcher.Match(approved.Snapshot, approved.Plan, inventory)
                    .Single(w => w.WindowId == item.WindowId);
                if (current.Status != ExistingRestoreStatus.Ready || current.Token != item.Token)
                {
                    Record(Outcome(ExistingRestoreStatus.Changed, "The approved running window changed or is no longer a unique match."));
                    continue;
                }

                var outcome = environment.Apply(approved.Snapshot.DesktopId, approved.Plan.Current, item, cancellation);
                Record(outcome);
                if (outcome.Status is ExistingRestoreStatus.Changed or ExistingRestoreStatus.Failed) stopped = true;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                Record(Outcome(ExistingRestoreStatus.Cancelled, "Cancelled before the next placement request."));
            }
            catch (Exception ex)
            {
                stopped = true;
                Record(Outcome(ExistingRestoreStatus.Failed, $"Restore stopped: {ex.Message}"));
            }
        }
        return results;
    }

    internal static bool SameDisplays(CurrentDisplayTopology a, CurrentDisplayTopology b)
    {
        if (a.Monitors.Count != b.Monitors.Count) return false;
        var first = a.Monitors.OrderBy(m => m.SourceName, StringComparer.OrdinalIgnoreCase).ToArray();
        var second = b.Monitors.OrderBy(m => m.SourceName, StringComparer.OrdinalIgnoreCase).ToArray();
        return first.Zip(second).All(pair =>
            (pair.First with { Devices = Array.Empty<ProbeDisplayDevice>() }) ==
            (pair.Second with { Devices = Array.Empty<ProbeDisplayDevice>() })
            && pair.First.Devices.OrderBy(d => d.InterfacePath, StringComparer.OrdinalIgnoreCase)
                .SequenceEqual(pair.Second.Devices.OrderBy(d => d.InterfacePath, StringComparer.OrdinalIgnoreCase)));
    }
}

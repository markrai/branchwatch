namespace BranchWatch.Tests;

[TestClass]
public sealed class ExistingWindowRestoreTests
{
    private static readonly Guid Desktop = Guid.Parse("3d58d290-9c50-4058-bd85-0c039b9c1414");
    private static readonly Guid OtherDesktop = Guid.Parse("7c58d290-9c50-4058-bd85-0c039b9c1414");
    private static readonly DateTimeOffset Captured = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly ProbeRect Screen = new(0, 0, 1920, 1080);
    private static readonly ProbeRect Desired = new(100, 100, 700, 600);
    private static readonly SnapshotMonitor Monitor = new("main", Screen, Screen, true, 0, 1920, 1080, 60, 100,
        [new ProbeDisplayDevice("physical-a", 1, "Monitor")]);
    private static readonly CurrentDisplayTopology Topology = new(Captured, [Monitor], []);

    [TestMethod]
    public void Match_UsesExactPathClassTitleAndActiveDesktop()
    {
        var first = Saved("Project A");
        var second = Saved("Project B", 1);
        var snapshot = Snapshot(first, second);
        var inventory = Inventory(Live(first, 1), Live(second, 2), Live(first, 3, OtherDesktop),
            Live(first, 4) with { Window = Live(first, 4).Window with { ExecutablePath = @"C:\Other\Editor.exe" } });
        var result = Match(snapshot, inventory);
        Assert.AreEqual(ExistingRestoreStatus.Ready, result[0].Status);
        Assert.AreEqual(ExistingRestoreStatus.Ready, result[1].Status);
        Assert.AreEqual(new IntPtr(1), result[0].Token!.Handle);
        Assert.AreEqual(new IntPtr(2), result[1].Token!.Handle);
        var changed = Live(first, 1);
        var noMatch = Match(Snapshot(first), Inventory(changed with { Window = changed.Window with { Title = "Renamed" } }));
        Assert.AreEqual(ExistingRestoreStatus.MissingWindow, noMatch[0].Status);
    }

    [TestMethod]
    public void Match_RejectsDuplicateSavedAndRunningIdentities()
    {
        var first = Saved("Project A");
        var duplicate = first with { Id = Guid.NewGuid(), EnumerationIndex = 1 };
        Assert.IsTrue(Match(Snapshot(first, duplicate), Inventory(Live(first, 1)))
            .All(i => i.Status == ExistingRestoreStatus.AmbiguousWindow));
        Assert.AreEqual(ExistingRestoreStatus.AmbiguousWindow,
            Match(Snapshot(first), Inventory(Live(first, 1), Live(first, 2)))[0].Status);
        var nonNormal = Live(first, 2);
        Assert.AreEqual(ExistingRestoreStatus.AmbiguousWindow,
            Match(Snapshot(first), Inventory(Live(first, 1), nonNormal with { Window = nonNormal.Window with { Minimized = true } }))[0].Status);
    }

    [TestMethod]
    public void Match_RejectsIncompleteIdentityAndUnverifiedOrNonNormalWindows()
    {
        var saved = Saved("Project A");
        var live = Live(saved, 1);
        Assert.AreEqual(ExistingRestoreStatus.MissingIdentity,
            Match(Snapshot(saved with { Title = null }), Inventory(live))[0].Status);
        Assert.AreEqual(ExistingRestoreStatus.MissingIdentity,
            Match(Snapshot(saved with { Title = new string('a', 512) }), Inventory(live))[0].Status);
        Assert.AreEqual(ExistingRestoreStatus.UnsupportedState,
            Match(Snapshot(saved with { Maximized = true }), Inventory(live))[0].Status);
        Assert.AreEqual(ExistingRestoreStatus.UnsupportedState,
            Match(Snapshot(saved), Inventory(live with { Window = live.Window with { Minimized = true } }))[0].Status);
        Assert.AreEqual(ExistingRestoreStatus.UnverifiedWindow,
            Match(Snapshot(saved), Inventory(live with { Token = null }))[0].Status);
        Assert.AreEqual(ExistingRestoreStatus.UnverifiedWindow,
            Match(Snapshot(saved), Inventory(live with { Window = live.Window with { ToolWindow = true } }))[0].Status);
        Assert.AreEqual(ExistingRestoreStatus.UnverifiedWindow,
            Match(Snapshot(saved), Inventory(live with { Window = live.Window with
                { Membership = new ProbeDesktopMembership(Desktop, 0, false, 0) } }))[0].Status);
    }

    [TestMethod]
    public void Prepare_RejectsChangedDesktopAndStaleMonitorPreview()
    {
        var saved = Saved("Project A");
        var snapshot = Snapshot(saved);
        var plan = Plan(snapshot);
        var env = new FakeEnvironment(Inventory(Live(saved, 1)) with { ActiveDesktopId = OtherDesktop });
        var service = new ExistingWindowRestoreService(env);
        Assert.ThrowsExactly<InvalidOperationException>(() => service.Prepare(snapshot, plan, UnmatchedMonitorPolicy.Skip, default));
        env.Current = Inventory(Live(saved, 1)) with { Topology = ChangedTopology() };
        Assert.ThrowsExactly<InvalidOperationException>(() => service.Prepare(snapshot, plan, UnmatchedMonitorPolicy.Skip, default));
        Assert.HasCount(0, env.Applied);
    }

    [TestMethod]
    public void Execute_RevalidatesApprovedTokenAndNeverSubstitutesAReplacementWindow()
    {
        var saved = Saved("Project A");
        var snapshot = Snapshot(saved);
        var live = Live(saved, 1);
        var env = new FakeEnvironment(Inventory(live));
        var service = new ExistingWindowRestoreService(env);
        var prepared = service.Prepare(snapshot, Plan(snapshot), UnmatchedMonitorPolicy.Skip, default);
        env.Current = Inventory(live with { Token = live.Token! with { CreatedFileTime = 999 } });
        var result = service.Execute(prepared, default);
        Assert.AreEqual(ExistingRestoreStatus.Changed, result.Single().Status);
        Assert.HasCount(0, env.Applied);
    }

    [TestMethod]
    public void Execute_StopsRemainingMovesWhenDesktopOrTopologyChanges()
    {
        var a = Saved("A");
        var b = Saved("B", 1);
        var c = Saved("C", 2);
        var snapshot = Snapshot(a, b, c);
        var inventory = Inventory(Live(a, 1), Live(b, 2), Live(c, 3));
        foreach (var drift in new[] { inventory with { ActiveDesktopId = OtherDesktop }, inventory with { Topology = ChangedTopology() } })
        {
            var env = new FakeEnvironment(inventory);
            var service = new ExistingWindowRestoreService(env);
            var prepared = service.Prepare(snapshot, Plan(snapshot), UnmatchedMonitorPolicy.Skip, default);
            env.AfterApply = () => env.Current = drift;
            var result = service.Execute(prepared, default);
            Assert.AreEqual(ExistingRestoreStatus.Restored, result[0].Status);
            Assert.AreEqual(ExistingRestoreStatus.Changed, result[1].Status);
            Assert.AreEqual(ExistingRestoreStatus.NotAttempted, result[2].Status);
            Assert.HasCount(1, env.Applied);
        }
    }

    [TestMethod]
    public void Execute_CancellationAndFailureStopSubsequentRequests()
    {
        var a = Saved("A");
        var b = Saved("B", 1);
        var snapshot = Snapshot(a, b);
        var inventory = Inventory(Live(a, 1), Live(b, 2));
        using var cancellation = new CancellationTokenSource();
        var env = new FakeEnvironment(inventory) { AfterApply = cancellation.Cancel };
        var service = new ExistingWindowRestoreService(env);
        var prepared = service.Prepare(snapshot, Plan(snapshot), UnmatchedMonitorPolicy.Skip, default);
        var result = service.Execute(prepared, cancellation.Token);
        Assert.AreEqual(ExistingRestoreStatus.Restored, result[0].Status);
        Assert.AreEqual(ExistingRestoreStatus.Cancelled, result[1].Status);
        Assert.HasCount(1, env.Applied);

        env = new FakeEnvironment(inventory) { ApplyStatus = ExistingRestoreStatus.Failed };
        service = new ExistingWindowRestoreService(env);
        prepared = service.Prepare(snapshot, Plan(snapshot), UnmatchedMonitorPolicy.Skip, default);
        result = service.Execute(prepared, default);
        Assert.AreEqual(ExistingRestoreStatus.Failed, result[0].Status);
        Assert.AreEqual(ExistingRestoreStatus.NotAttempted, result[1].Status);
        Assert.HasCount(1, env.Applied);
    }

    [TestMethod]
    public void Execute_ReportsAlreadyPlacedAndDoesNotAttemptMissingWindows()
    {
        var a = Saved("A");
        var b = Saved("B", 1);
        var snapshot = Snapshot(a, b);
        var env = new FakeEnvironment(Inventory(Live(a, 1))) { ApplyStatus = ExistingRestoreStatus.AlreadyPlaced };
        var service = new ExistingWindowRestoreService(env);
        var prepared = service.Prepare(snapshot, Plan(snapshot), UnmatchedMonitorPolicy.Skip, default);
        var result = service.Execute(prepared, default);
        Assert.AreEqual(ExistingRestoreStatus.AlreadyPlaced, result[0].Status);
        Assert.AreEqual(ExistingRestoreStatus.MissingWindow, result[1].Status);
        Assert.HasCount(1, env.Applied);
        Assert.AreEqual(a.Id, env.Applied[0]);
    }

    [TestMethod]
    public void Selection_ExecutesOnlySelectedWindowAndReportsEveryOutcome()
    {
        var a = Saved("A");
        var b = Saved("B", 1);
        var snapshot = Snapshot(a, b);
        var env = new FakeEnvironment(Inventory(Live(a, 1), Live(b, 2)));
        var service = new ExistingWindowRestoreService(env);
        var prepared = RestoreSelection.Apply(service.Prepare(snapshot, Plan(snapshot), UnmatchedMonitorPolicy.Skip, default),
            new HashSet<Guid> { b.Id });
        var progress = new List<ExistingRestoreOutcome>();
        var result = service.Execute(prepared, default, progress.Add);
        Assert.AreEqual(ExistingRestoreStatus.UserSkipped, result[0].Status);
        Assert.AreEqual(ExistingRestoreStatus.Restored, result[1].Status);
        CollectionAssert.AreEqual(new[] { b.Id }, env.Applied.ToArray());
        CollectionAssert.AreEqual(result.ToArray(), progress.ToArray());
        Assert.HasCount(2, prepared.Snapshot.Windows);
    }

    [TestMethod]
    public void Selection_DoesNotRemoveDuplicateIdentityEvidence()
    {
        var a = Saved("A");
        var b = a with { Id = Guid.NewGuid(), EnumerationIndex = 1 };
        var snapshot = Snapshot(a, b);
        var env = new FakeEnvironment(Inventory(Live(a, 1)));
        var service = new ExistingWindowRestoreService(env);
        var prepared = RestoreSelection.Apply(service.Prepare(snapshot, Plan(snapshot), UnmatchedMonitorPolicy.Skip, default),
            new HashSet<Guid> { a.Id });
        var result = service.Execute(prepared, default);
        Assert.AreEqual(ExistingRestoreStatus.AmbiguousWindow, result[0].Status);
        Assert.AreEqual(ExistingRestoreStatus.UserSkipped, result[1].Status);
        Assert.HasCount(0, env.Applied);
    }

    [TestMethod]
    public void Selection_DefaultsExcludeReviewAndUnsupportedAndRetainOnlyUnchangedPlacements()
    {
        var a = Saved("A");
        var b = Saved("B", 1);
        var c = Saved("C", 2) with { Minimized = true };
        var snapshot = Snapshot(a, b, c);
        var original = Plan(snapshot);
        var changed = original with
        {
            Windows = original.Windows.Select(w => w.WindowId == b.Id ? w with
                { RequiresReview = true, Kind = WindowPlacementKind.PrimaryFallback } : w).ToArray()
        };
        CollectionAssert.AreEquivalent(new[] { a.Id }, RestoreSelection.Default(changed).ToArray());
        CollectionAssert.AreEquivalent(new[] { a.Id }, RestoreSelection.Retain(original, changed,
            RestoreSelection.Default(original)).ToArray());
        Assert.IsFalse(RestoreSelection.CanSelect(changed.Windows.Single(w => w.WindowId == c.Id)));
        var env = new FakeEnvironment(Inventory());
        var service = new ExistingWindowRestoreService(env);
        var preparation = service.Prepare(snapshot, original, UnmatchedMonitorPolicy.Skip, default);
        Assert.ThrowsExactly<InvalidOperationException>(() => RestoreSelection.Apply(preparation, new HashSet<Guid> { c.Id }));
        Assert.ThrowsExactly<InvalidOperationException>(() => RestoreSelection.Apply(preparation, new HashSet<Guid> { Guid.NewGuid() }));
    }

    [TestMethod]
    public void ProgressObserverFailure_DoesNotChangeRestoreOutcome()
    {
        var a = Saved("A");
        var b = Saved("B", 1);
        var snapshot = Snapshot(a, b);
        var env = new FakeEnvironment(Inventory(Live(a, 1), Live(b, 2)));
        var service = new ExistingWindowRestoreService(env);
        var prepared = service.Prepare(snapshot, Plan(snapshot), UnmatchedMonitorPolicy.Skip, default);
        var result = service.Execute(prepared, default, _ => throw new InvalidOperationException("Observer failure"));
        Assert.HasCount(2, result);
        Assert.IsTrue(result.All(r => r.Status == ExistingRestoreStatus.Restored));
        Assert.HasCount(2, env.Applied);
    }

    private static SnapshotWindow Saved(string title, int index = 0) =>
        new(Guid.NewGuid(), "Editor", @"C:\Apps\Editor.exe", title, "EditorWindow", Desired, null, Desired,
            false, false, 1, 0, "main", 96, index, ["main"]);

    private static DesktopSnapshot Snapshot(params SnapshotWindow[] windows) =>
        new(Desktop, "Work", Captured, [Monitor], windows, 0, []);

    private static DesktopPlacementPlan Plan(DesktopSnapshot snapshot) => DesktopPlacementPlanner.Plan(snapshot, Topology);
    private static ExistingRestoreInventory Inventory(params RestoreLiveWindow[] windows) => new(Desktop, Topology, windows);
    private static IReadOnlyList<ExistingRestoreItem> Match(DesktopSnapshot snapshot, ExistingRestoreInventory inventory) =>
        ExistingWindowRestoreMatcher.Match(snapshot, Plan(snapshot), inventory);
    private static CurrentDisplayTopology ChangedTopology() =>
        Topology with { Monitors = [Monitor with { ScalePercent = 125 }] };

    private static RestoreLiveWindow Live(SnapshotWindow saved, int handle, Guid? desktop = null)
    {
        var id = desktop ?? Desktop;
        var bounds = new ProbeRect(200, 200, 800, 700);
        var window = new ProbeWindow($"0x{handle:X}", (uint)handle, saved.ProcessName, saved.ExecutablePath, saved.Title, saved.ClassName,
            true, false, false, true, false, false, 0, 0, null, bounds, null, null, 0, bounds, null, 1, 0, "main", 96,
            new ProbeDesktopMembership(id, 0, id == Desktop, 0), "candidate", handle);
        return new(window, new RestoreWindowToken(new IntPtr(handle), (uint)handle, 10, 100));
    }

    private sealed class FakeEnvironment(ExistingRestoreInventory inventory) : IExistingWindowRestoreEnvironment
    {
        internal ExistingRestoreInventory Current { get; set; } = inventory;
        internal List<Guid> Applied { get; } = [];
        internal Action? AfterApply { get; set; }
        internal ExistingRestoreStatus ApplyStatus { get; set; } = ExistingRestoreStatus.Restored;

        public ExistingRestoreInventory Inspect() => Current;

        public ExistingRestoreOutcome Apply(Guid desktopId, CurrentDisplayTopology topology, ExistingRestoreItem item, CancellationToken cancellation)
        {
            Assert.AreEqual(Desktop, desktopId);
            Assert.IsTrue(ExistingWindowRestoreService.SameDisplays(Topology, topology));
            cancellation.ThrowIfCancellationRequested();
            Applied.Add(item.WindowId);
            AfterApply?.Invoke();
            return new(item.WindowId, item.Label, ApplyStatus, "Test outcome");
        }
    }
}

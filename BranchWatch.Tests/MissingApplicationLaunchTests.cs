namespace BranchWatch.Tests;

[TestClass]
public sealed class MissingApplicationLaunchTests
{
    private static readonly Guid Desktop = Guid.Parse("3d58d290-9c50-4058-bd85-0c039b9c1414");
    private static readonly Guid Other = Guid.Parse("7c58d290-9c50-4058-bd85-0c039b9c1414");
    private static readonly DateTimeOffset Captured = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly ProbeRect Bounds = new(100, 100, 700, 600);
    private static readonly ProbeRect Screen = new(0, 0, 1920, 1080);
    private static readonly SnapshotMonitor Monitor = new("main", Screen, Screen, true, 0, 1920, 1080, 60, 100,
        [new ProbeDisplayDevice("physical-a", 1, "Monitor")]);
    private static readonly CurrentDisplayTopology Topology = new(Captured, [Monitor], []);
    private static readonly LaunchedProcessIdentity ProcessIdentity = new(42, 123456);

    [TestMethod]
    public void Prepare_BlocksMultipleSavedWindowsAndRunningApplicationOnAnotherDesktop()
    {
        var first = Saved("One");
        var second = Saved("Two", first.ExecutablePath!, 1);
        var environment = new FakeEnvironment(Inventory());
        var service = new MissingApplicationLaunchService(environment, environment);
        var duplicate = service.Prepare(Prepare(environment, first, second), default);
        Assert.HasCount(2, duplicate.Items);
        Assert.IsTrue(duplicate.Items.All(i => i.Status == ExistingRestoreStatus.LaunchBlocked));

        environment.Current = Inventory(Live(first, 1, 5, 100, Other));
        var otherDesktop = service.Prepare(Prepare(environment, first), default);
        Assert.AreEqual(ExistingRestoreStatus.LaunchBlocked, otherDesktop.Items.Single().Status);
        Assert.AreEqual(0, environment.LaunchCount);
    }

    [TestMethod]
    public void Prepare_RespectsProcessAndExecutablePreflight()
    {
        var saved = Saved("One");
        var env = new FakeEnvironment(Inventory()) { BlockReason = "Application process already running." };
        var service = new MissingApplicationLaunchService(env, env);
        var plan = service.Prepare(Prepare(env, saved), default);
        Assert.AreEqual(ExistingRestoreStatus.LaunchBlocked, plan.Items.Single().Status);
        Assert.AreEqual(0, env.LaunchCount);
    }

    [TestMethod]
    public void Execute_LaunchesOnlyNewWindowFromDirectProcessAndVerifiesPlacement()
    {
        var saved = Saved("One");
        var env = new FakeEnvironment(Inventory());
        var service = new MissingApplicationLaunchService(env, env);
        var plan = service.Prepare(Prepare(env, saved), default);
        Assert.AreEqual(ExistingRestoreStatus.LaunchReady, plan.Items.Single().Status);
        env.AfterLaunch = () => env.Current = Inventory(Live(saved, 10, ProcessIdentity.ProcessId, ProcessIdentity.CreatedFileTime));
        var result = service.Execute(plan, MissingResults(plan), default);
        Assert.AreEqual(ExistingRestoreStatus.LaunchedAndRestored, result.Single().Status);
        Assert.AreEqual(1, env.LaunchCount);
        Assert.HasCount(1, env.Applied);
        Assert.AreEqual(new IntPtr(10), env.Applied.Single().Token!.Handle);

        var repeated = service.Prepare(Prepare(env, saved), default);
        Assert.HasCount(0, repeated.Items);
        Assert.AreEqual(1, env.LaunchCount);
    }

    [TestMethod]
    public void Execute_DoesNotLaunchIfApplicationAppearsAfterApproval()
    {
        var saved = Saved("One");
        var env = new FakeEnvironment(Inventory());
        var service = new MissingApplicationLaunchService(env, env);
        var plan = service.Prepare(Prepare(env, saved), default);
        env.Current = Inventory(Live(saved, 9, 99, 100));
        var result = service.Execute(plan, MissingResults(plan), default);
        Assert.AreEqual(ExistingRestoreStatus.LaunchBlocked, result.Single().Status);
        Assert.AreEqual(0, env.LaunchCount);
        Assert.HasCount(0, env.Applied);
    }

    [TestMethod]
    public void Execute_RejectsWrongProcessWrongCreationTimeAndPreviouslySeenHandle()
    {
        var saved = Saved("One");
        var unrelated = Saved("Other", @"C:\Apps\Other.exe");
        foreach (var invalid in new[]
        {
            Live(saved, 10, 99, ProcessIdentity.CreatedFileTime),
            Live(saved, 10, ProcessIdentity.ProcessId, 999),
            Live(saved, 11, ProcessIdentity.ProcessId, ProcessIdentity.CreatedFileTime)
        })
        {
            var env = new FakeEnvironment(Inventory(Live(unrelated, 11, 99, 100)));
            var service = new MissingApplicationLaunchService(env, env);
            var plan = service.Prepare(Prepare(env, saved), default);
            env.AfterLaunch = () => env.Current = Inventory(invalid);
            var result = service.Execute(plan, MissingResults(plan), default);
            Assert.AreEqual(ExistingRestoreStatus.LaunchUnverified, result.Single().Status);
            Assert.AreEqual(1, env.LaunchCount);
            Assert.HasCount(0, env.Applied);
        }
    }

    [TestMethod]
    public void Execute_TimeoutOrAmbiguityStopsFurtherLaunches()
    {
        var first = Saved("One");
        var second = Saved("Two", @"C:\Apps\Other.exe", 1);
        foreach (var ambiguous in new[] { false, true })
        {
            var env = new FakeEnvironment(Inventory());
            var service = new MissingApplicationLaunchService(env, env);
            var plan = service.Prepare(Prepare(env, first, second), default);
            if (ambiguous)
                env.AfterLaunch = () => env.Current = Inventory(Live(first, 10, 42, 123456), Live(first, 11, 42, 123456));
            var results = service.Execute(plan, MissingResults(plan), default);
            Assert.AreEqual(ExistingRestoreStatus.LaunchUnverified, results[0].Status);
            Assert.AreEqual(ExistingRestoreStatus.NotAttempted, results[1].Status);
            Assert.AreEqual(1, env.LaunchCount);
            Assert.HasCount(0, env.Applied);
        }
    }

    [TestMethod]
    public void Execute_CancellationAfterLaunchNeverMovesOrStartsAnotherProcess()
    {
        var first = Saved("One");
        var second = Saved("Two", @"C:\Apps\Other.exe", 1);
        var env = new FakeEnvironment(Inventory());
        var service = new MissingApplicationLaunchService(env, env);
        var plan = service.Prepare(Prepare(env, first, second), default);
        using var cancellation = new CancellationTokenSource();
        env.AfterLaunch = cancellation.Cancel;
        var results = service.Execute(plan, MissingResults(plan), cancellation.Token);
        Assert.IsTrue(results.All(r => r.Status == ExistingRestoreStatus.Cancelled));
        Assert.AreEqual(1, env.LaunchCount);
        Assert.HasCount(0, env.Applied);
    }

    [TestMethod]
    public void Execute_RejectsDesktopAndDisplayDriftBeforeLaunch()
    {
        var saved = Saved("One");
        foreach (var drift in new[]
        {
            Inventory() with { ActiveDesktopId = Other },
            Inventory() with { Topology = Topology with { Monitors = [Monitor with { ScalePercent = 125 }] } }
        })
        {
            var env = new FakeEnvironment(Inventory());
            var service = new MissingApplicationLaunchService(env, env);
            var plan = service.Prepare(Prepare(env, saved), default);
            env.Current = drift;
            var result = service.Execute(plan, MissingResults(plan), default);
            Assert.AreEqual(ExistingRestoreStatus.Changed, result.Single().Status);
            Assert.AreEqual(0, env.LaunchCount);
        }
    }

    [TestMethod]
    public void Execute_ExistingRestoreFailurePreventsLaunch()
    {
        var saved = Saved("One");
        var existing = Saved("Two", @"C:\Apps\Other.exe", 1);
        var env = new FakeEnvironment(Inventory(Live(existing, 11, 99, 100)));
        var service = new MissingApplicationLaunchService(env, env);
        var plan = service.Prepare(Prepare(env, saved, existing), default);
        var results = MissingResults(plan).Select(r => r.WindowId == existing.Id
            ? r with { Status = ExistingRestoreStatus.Failed } : r).ToArray();
        var result = service.Execute(plan, results, default);
        Assert.AreEqual(ExistingRestoreStatus.NotAttempted, result[0].Status);
        Assert.AreEqual(ExistingRestoreStatus.Failed, result[1].Status);
        Assert.AreEqual(0, env.LaunchCount);
    }


    [TestMethod]
    public void AdapterLaunch_UsesApprovedFolderSpecAndDirectProcessCorrelation()
    {
        var saved = Saved("Project A", @"C:\Apps\Code.exe") with
        {
            ClassName = "Chrome_WidgetWin_1",
            FolderContext = new ApplicationFolderContext(ApplicationFolderKind.VisualStudioCode, @"C:\Projects\Project A")
        };
        var env = new FakeEnvironment(Inventory());
        var service = new MissingApplicationLaunchService(env, env);
        var plan = service.Prepare(Prepare(env, saved), default);
        Assert.AreEqual(ExistingRestoreStatus.LaunchReady, plan.Items.Single().Status);
        env.AfterLaunch = () => env.Current = Inventory(Live(saved, 10, ProcessIdentity.ProcessId, ProcessIdentity.CreatedFileTime));
        var result = service.Execute(plan, MissingResults(plan), default);
        Assert.AreEqual(ExistingRestoreStatus.LaunchedAndRestored, result.Single().Status);
        CollectionAssert.AreEqual(new[] { "--new-window", @"C:\Projects\Project A" }, env.LastSpec!.Arguments.ToArray());
        Assert.HasCount(1, env.Applied);
    }

    [TestMethod]
    public void AdapterLaunch_BrokeredFolderRequestNeverClaimsPlacementAndStopsFurtherLaunches()
    {
        var saved = Saved("Folder", @"C:\Windows\explorer.exe") with
        {
            ClassName = "CabinetWClass",
            FolderContext = new ApplicationFolderContext(ApplicationFolderKind.Explorer, @"C:\Projects\One")
        };
        var later = Saved("Other", @"C:\Apps\Other.exe", 1);
        var env = new FakeEnvironment(Inventory());
        var service = new MissingApplicationLaunchService(env, env);
        var plan = service.Prepare(Prepare(env, saved, later), default);
        var result = service.Execute(plan, MissingResults(plan), default);
        Assert.AreEqual(ExistingRestoreStatus.LaunchUnverified, result[0].Status);
        Assert.AreEqual(ExistingRestoreStatus.NotAttempted, result[1].Status);
        Assert.AreEqual(1, env.OpenCount);
        Assert.AreEqual(0, env.LaunchCount);
        Assert.HasCount(0, env.Applied);
    }

    [TestMethod]
    public void AdapterLaunch_BlockedFolderNeverFallsBackToGenericLaunch()
    {
        var saved = Saved("Project A", @"C:\Apps\Code.exe") with
        {
            ClassName = "Chrome_WidgetWin_1",
            FolderContext = new ApplicationFolderContext(ApplicationFolderKind.VisualStudioCode, @"C:\Projects\Missing")
        };
        var env = new FakeEnvironment(Inventory()) { BlockReason = "Saved folder unavailable." };
        var service = new MissingApplicationLaunchService(env, env);
        var plan = service.Prepare(Prepare(env, saved), default);
        var result = service.Execute(plan, MissingResults(plan), default);
        Assert.AreEqual(ExistingRestoreStatus.LaunchBlocked, result.Single().Status);
        Assert.AreEqual(0, env.LaunchCount);
    }

    [TestMethod]
    public void Selection_UnselectedMissingApplicationCannotBeLaunched()
    {
        var a = Saved("One");
        var b = Saved("Two", @"C:\Apps\Other.exe", 1);
        var env = new FakeEnvironment(Inventory());
        var service = new MissingApplicationLaunchService(env, env);
        var preparation = RestoreSelection.Apply(Prepare(env, a, b), new HashSet<Guid> { b.Id });
        var plan = service.Prepare(preparation, default);
        Assert.AreEqual(b.Id, plan.Items.Single().RestoreItem.WindowId);
        env.AfterLaunch = () => env.Current = Inventory(Live(b, 10, ProcessIdentity.ProcessId, ProcessIdentity.CreatedFileTime));
        var progress = new List<ExistingRestoreOutcome>();
        var result = service.Execute(plan, MissingResults(plan), default, progress.Add);
        Assert.AreEqual(ExistingRestoreStatus.UserSkipped, result[0].Status);
        Assert.AreEqual(ExistingRestoreStatus.LaunchedAndRestored, result[1].Status);
        Assert.AreEqual(b.ExecutablePath, env.LastSpec!.ExecutablePath);
        Assert.AreEqual(1, env.LaunchCount);
        Assert.HasCount(1, progress);
        Assert.AreEqual(result[1], progress.Single());
    }

    private static SnapshotWindow Saved(string title, string path = @"C:\Apps\Editor.exe", int index = 0) =>
        new(Guid.NewGuid(), "Editor", path, title, "EditorWindow", Bounds, null, Bounds,
            false, false, 1, 0, "main", 96, index, ["main"]);

    private static ExistingRestoreInventory Inventory(params RestoreLiveWindow[] windows) => new(Desktop, Topology, windows);

    private static ExistingRestorePreparation Prepare(FakeEnvironment environment, params SnapshotWindow[] windows)
    {
        var snapshot = new DesktopSnapshot(Desktop, "Work", Captured, [Monitor], windows, 0, []);
        return new ExistingWindowRestoreService(environment).Prepare(snapshot,
            DesktopPlacementPlanner.Plan(snapshot, Topology), UnmatchedMonitorPolicy.Skip, default);
    }

    private static IReadOnlyList<ExistingRestoreOutcome> MissingResults(MissingApplicationLaunchPreparation plan) =>
        plan.Existing.Items.Select(i => new ExistingRestoreOutcome(i.WindowId, i.Label, i.Status, i.Detail)).ToArray();

    private static RestoreLiveWindow Live(SnapshotWindow saved, int handle, uint pid, long creation, Guid? desktop = null)
    {
        var id = desktop ?? Desktop;
        var window = new ProbeWindow($"0x{handle:X}", pid, saved.ProcessName, saved.ExecutablePath, saved.Title, saved.ClassName,
            true, false, false, true, false, false, 0, 0, null, Bounds, null, null, 0, Bounds, null, 1, 0, "main", 96,
            new ProbeDesktopMembership(id, 0, id == Desktop, 0), "candidate", handle);
        return new(window, new RestoreWindowToken(new IntPtr(handle), pid, 10, creation));
    }

    private sealed class FakeEnvironment(ExistingRestoreInventory inventory) : IExistingWindowRestoreEnvironment,
        IMissingApplicationLaunchEnvironment
    {
        internal ExistingRestoreInventory Current { get; set; } = inventory;
        internal List<ExistingRestoreItem> Applied { get; } = [];
        internal int LaunchCount { get; private set; }
        internal int OpenCount { get; private set; }
        internal ApplicationLaunchSpec? LastSpec { get; private set; }
        internal string? BlockReason { get; set; }
        internal Action? AfterLaunch { get; set; }
        public ExistingRestoreInventory Inspect() => Current;
        public string? CheckLaunchBlock(string path) => BlockReason;
        public string? CheckLaunchBlock(ApplicationLaunchSpec spec) => BlockReason;
        public LaunchedProcessIdentity Launch(ApplicationLaunchSpec spec, Guid desktopId, CurrentDisplayTopology topology, CancellationToken cancellation)
        {
            LastSpec = spec;
            return Launch(spec.ExecutablePath, desktopId, topology, cancellation);
        }
        public void OpenFolder(ApplicationLaunchSpec spec, Guid desktopId, CurrentDisplayTopology topology, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            LastSpec = spec;
            OpenCount++;
        }

        public LaunchedProcessIdentity Launch(string path, Guid desktopId, CurrentDisplayTopology topology, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            LaunchCount++;
            AfterLaunch?.Invoke();
            return ProcessIdentity;
        }

        public bool IsRunning(LaunchedProcessIdentity process) => process == ProcessIdentity;
        public bool WaitForInventory(CancellationToken cancellation) => !cancellation.IsCancellationRequested;

        public ExistingRestoreOutcome Apply(Guid desktopId, CurrentDisplayTopology topology,
            ExistingRestoreItem item, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            Applied.Add(item);
            return new(item.WindowId, item.Label, ExistingRestoreStatus.Restored, "Verified test placement");
        }
    }
}

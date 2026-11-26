using System.IO;

namespace BranchWatch.Tests;

[TestClass]
public sealed class DesktopMemoryPlacementTests
{
    private static readonly Guid DesktopId = Guid.Parse("c2d5479e-9d54-4e3a-9cad-249f89b6468d");
    private static readonly DateTimeOffset Captured = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly SnapshotMonitor Main = Monitor("main", "physical-a", new(0, 0, 1920, 1080), true);
    private static readonly SnapshotMonitor Side = Monitor("side", "physical-b", new(-1080, 0, 0, 1920), false, 90, 125);

    [TestMethod]
    public void Match_UsesUniquePhysicalIdentityInsteadOfDisplayNameOrPrimary()
    {
        var renamedMain = Main with { SourceName = "side", Primary = false };
        var renamedSide = Side with { SourceName = "main", Primary = true };
        var result = MonitorMatcher.Match([Main, Side], [renamedMain, renamedSide]);
        Assert.AreEqual("side", result[0].CurrentSource);
        Assert.AreEqual("main", result[1].CurrentSource);
        Assert.IsTrue(result.All(m => m.Kind == MonitorMatchKind.PhysicalIdentity));
        var replaced = Side with { Devices = [new ProbeDisplayDevice("replacement", 1, "Same friendly name")] };
        Assert.AreEqual(MonitorMatchKind.Missing, MonitorMatcher.Match([Side], [replaced])[0].Kind);
    }

    [TestMethod]
    public void Match_RejectsMissingAndDuplicateIdentitiesAndClonedSources()
    {
        var duplicate = Side with { Devices = Main.Devices };
        Assert.AreEqual(MonitorMatchKind.Ambiguous, MonitorMatcher.Match([Main], [Main, duplicate])[0].Kind);
        Assert.AreEqual(MonitorMatchKind.Ambiguous, MonitorMatcher.Match([Main, duplicate], [Main])[0].Kind);
        var clone = Main with { Devices = [Main.Devices[0], Side.Devices[0]] };
        Assert.AreEqual(MonitorMatchKind.Ambiguous, MonitorMatcher.Match([Main], [clone])[0].Kind);
        var partial = Main with { Devices = [Main.Devices[0], new ProbeDisplayDevice(null, 1, null)] };
        Assert.AreEqual(MonitorMatchKind.Ambiguous, MonitorMatcher.Match([Main], [partial])[0].Kind);
        var unknown = Main with { Devices = [] };
        Assert.AreEqual(MonitorMatchKind.IdentityUnavailable, MonitorMatcher.Match([unknown], [Main])[0].Kind);
        Assert.AreEqual(MonitorMatchKind.IdentityUnavailable, MonitorMatcher.Match([Main], [unknown])[0].Kind);
    }

    [TestMethod]
    public void Plan_PreservesExactGeometryAndTranslatesRenumberedPhysicalMonitor()
    {
        var mainWindow = Window("main", new(100, 200, 700, 800));
        var sideWindow = Window("side", new(-1000, 100, -200, 900), index: 1);
        var snapshot = Snapshot([Main, Side], [mainWindow, sideWindow]);
        var exact = DesktopPlacementPlanner.Plan(snapshot, Topology(Main, Side));
        Assert.IsTrue(exact.Windows.All(w => w.Kind == WindowPlacementKind.Exact && !w.RequiresReview));
        Assert.AreEqual<ProbeRect?>(mainWindow.ScreenBounds, exact.Windows[0].ScreenBounds);
        var movedSide = Side with { SourceName = "display3", Bounds = new(1920, -200, 3000, 1720), WorkArea = new(1920, -200, 3000, 1720) };
        var translated = DesktopPlacementPlanner.Plan(snapshot, Topology(Main, movedSide));
        Assert.AreEqual(WindowPlacementKind.Translated, translated.Windows[1].Kind);
        Assert.AreEqual("display3", translated.Windows[1].TargetSource);
        Assert.AreEqual<ProbeRect?>(new ProbeRect(2000, -100, 2800, 700), translated.Windows[1].ScreenBounds);
    }

    [TestMethod]
    public void Plan_AdaptsRotationResolutionDpiAndWorkAreaWithoutDoubleScaling()
    {
        var saved = Main with { WorkArea = new(0, 40, 1920, 1080) };
        var target = Main with { Bounds = new(0, 0, 1080, 1920), WorkArea = new(0, 0, 1080, 1880), RotationDegrees = 90, ScalePercent = 150 };
        var window = Window("main", new(480, 300, 1440, 820));
        var plan = DesktopPlacementPlanner.Plan(Snapshot([saved], [window]), Topology(target));
        Assert.AreEqual(WindowPlacementKind.Adapted, plan.Windows[0].Kind);
        Assert.AreEqual<ProbeRect?>(new ProbeRect(270, 470, 810, 1410), plan.Windows[0].ScreenBounds);
        Assert.IsTrue(plan.Windows[0].RequiresReview);

        var dpiOnly = Main with { ScalePercent = 150 };
        var unchangedPixels = DesktopPlacementPlanner.Plan(Snapshot([Main], [window]), Topology(dpiOnly));
        Assert.AreEqual<ProbeRect?>(window.ScreenBounds, unchangedPixels.Windows[0].ScreenBounds);
        Assert.AreEqual(WindowPlacementKind.Adapted, unchangedPixels.Windows[0].Kind);
    }

    [TestMethod]
    public void Plan_MissingMonitorDefaultsToSkipAndExplicitFallbackFitsPrimary()
    {
        var window = Window("side", new(-1000, 100, -100, 1700));
        var snapshot = Snapshot([Main, Side], [window]);
        var skipped = DesktopPlacementPlanner.Plan(snapshot, Topology(Main));
        Assert.AreEqual(WindowPlacementKind.Skipped, skipped.Windows[0].Kind);
        Assert.IsNull(skipped.Windows[0].ScreenBounds);
        var fallback = DesktopPlacementPlanner.Plan(snapshot, Topology(Main), UnmatchedMonitorPolicy.Primary);
        Assert.AreEqual(WindowPlacementKind.PrimaryFallback, fallback.Windows[0].Kind);
        Assert.AreEqual("main", fallback.Windows[0].TargetSource);
        Assert.IsTrue(fallback.Windows[0].RequiresReview);
        Assert.IsTrue(Contains(Main.WorkArea, fallback.Windows[0].ScreenBounds!.Value));
        Assert.AreEqual(new ProbeRect(-1000, 100, -100, 1700), snapshot.Windows[0].ScreenBounds);
    }

    [TestMethod]
    public void Plan_SpanningWindowsPreservedOnlyAcrossCompatibleCoveredDisplays()
    {
        var window = Window("main", new(-300, 100, 500, 800));
        var snapshot = Snapshot([Main, Side], [window]);
        var exact = DesktopPlacementPlanner.Plan(snapshot, Topology(Main, Side));
        Assert.AreEqual(WindowPlacementKind.Exact, exact.Windows[0].Kind);
        var movedSide = Side with { Bounds = new(-1180, 0, -100, 1920), WorkArea = new(-1180, 0, -100, 1920) };
        var adapted = DesktopPlacementPlanner.Plan(snapshot, Topology(Main, movedSide));
        Assert.AreEqual(WindowPlacementKind.Adapted, adapted.Windows[0].Kind);
        Assert.IsTrue(adapted.Windows[0].RequiresReview);
        Assert.IsTrue(Contains(Main.WorkArea, adapted.Windows[0].ScreenBounds!.Value));
        Assert.IsFalse(DesktopPlacementPlanner.FullyCovered(new(-300, 100, 500, 800), [Main.WorkArea, movedSide.WorkArea]));
        Assert.IsTrue(DesktopPlacementPlanner.FullyCovered(new(-300, 100, 500, 800), [Main.WorkArea, Side.WorkArea]));
        Assert.IsFalse(DesktopPlacementPlanner.FullyCovered(new(-300, 100, 500, 800), [new(-300, 100, 500, 400), new(-300, 500, 500, 800)]));
    }

    [TestMethod]
    public void Plan_NonNormalStatesUseOnlyReviewableEstimatesOrSkip()
    {
        var saved = Main with { WorkArea = new(0, 40, 1920, 1080) };
        var minimized = Window("main", new(-32000, -32000, -31900, -31900)) with
        {
            Minimized = true, NormalPlacementBounds = new(100, 100, 700, 500)
        };
        var maximized = Window("main", Main.Bounds, 1) with
        {
            Maximized = true, NormalPlacementBounds = new(-5000, -5000, -4000, -4000)
        };
        var plan = DesktopPlacementPlanner.Plan(Snapshot([saved], [minimized, maximized]), Topology(saved));
        Assert.AreEqual(PlannedWindowState.Minimized, plan.Windows[0].State);
        Assert.AreEqual<ProbeRect?>(new ProbeRect(100, 140, 700, 540), plan.Windows[0].ScreenBounds);
        Assert.IsTrue(plan.Windows[0].RequiresReview);
        Assert.AreEqual(PlannedWindowState.Maximized, plan.Windows[1].State);
        Assert.AreEqual(WindowPlacementKind.Skipped, plan.Windows[1].Kind);
        Assert.IsNull(plan.Windows[1].ScreenBounds);
    }

    [TestMethod]
    public void Plan_ClampsOffscreenAndExtremeCoordinatesAndRejectsInvalidTopology()
    {
        var window = Window("main", new(-5000, 2000, 6000, 5000));
        var plan = DesktopPlacementPlanner.Plan(Snapshot([Main], [window]), Topology(Main));
        Assert.IsTrue(Contains(Main.WorkArea, plan.Windows[0].ScreenBounds!.Value));
        var extreme = DesktopPlacementPlanner.Adapt(new(int.MinValue, int.MinValue, int.MaxValue, int.MaxValue),
            new(0, 0, 1, 1), new(int.MinValue, int.MinValue, int.MaxValue, int.MaxValue));
        Assert.AreEqual(new ProbeRect(int.MinValue, int.MinValue, int.MaxValue, int.MaxValue), extreme);
        Assert.ThrowsExactly<InvalidDataException>(() => DesktopPlacementPlanner.Plan(Snapshot([Main], [window]), Topology()));
        Assert.ThrowsExactly<InvalidDataException>(() => DesktopPlacementPlanner.Plan(Snapshot([Main], [window]), Topology(Main, Main)));
        Assert.ThrowsExactly<InvalidDataException>(() => DesktopPlacementPlanner.Plan(Snapshot([Main], [window]),
            Topology(Main with { WorkArea = new(-1, 0, 1920, 1080) })));
    }

    [TestMethod]
    public void Plan_IsDeterministicAndDoesNotMutateSnapshot()
    {
        var snapshot = Snapshot([Main, Side], [Window("main", new(100, 100, 900, 900))]);
        var current = Topology(Main, Side);
        var first = DesktopPlacementPlanner.Plan(snapshot, current);
        var second = DesktopPlacementPlanner.Plan(snapshot, current);
        CollectionAssert.AreEqual(first.Windows.ToArray(), second.Windows.ToArray());
        CollectionAssert.AreEqual(first.MonitorMatches.ToArray(), second.MonitorMatches.ToArray());
        Assert.AreEqual(new ProbeRect(100, 100, 900, 900), snapshot.Windows[0].ScreenBounds);
    }

    private static bool Contains(ProbeRect a, ProbeRect b) =>
        a.Left <= b.Left && a.Top <= b.Top && a.Right >= b.Right && a.Bottom >= b.Bottom;

    private static SnapshotMonitor Monitor(string source, string id, ProbeRect bounds, bool primary, int rotation = 0, int scale = 100) =>
        new(source, bounds, bounds, primary, rotation, (int)bounds.Width, (int)bounds.Height, 60, scale,
            [new ProbeDisplayDevice(id, 1, "Display")]);

    private static SnapshotWindow Window(string source, ProbeRect bounds, int index = 0) =>
        new(Guid.NewGuid(), "Editor", @"C:\Apps\Editor.exe", "Editor", "EditorWindow", bounds, null, bounds,
            false, false, 1, 0, source, 96, index, [source]);

    private static DesktopSnapshot Snapshot(IReadOnlyList<SnapshotMonitor> monitors, IReadOnlyList<SnapshotWindow> windows) =>
        new(DesktopId, "Work", Captured, monitors, windows, 0, []);

    private static CurrentDisplayTopology Topology(params SnapshotMonitor[] monitors) => new(Captured, monitors, []);
}

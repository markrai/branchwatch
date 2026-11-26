using System.IO;
using System.Text.Json;

namespace BranchWatch.Tests;

[TestClass]
public sealed class DesktopMemorySnapshotTests
{
    private static readonly Guid Desktop = Guid.Parse("3d58d290-9c50-4058-bd85-0c039b9c1414");
    private static readonly Guid Other = Guid.Parse("7c58d290-9c50-4058-bd85-0c039b9c1414");
    private static readonly DateTimeOffset Captured = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly ProbeRect MainBounds = new(0, 0, 1920, 1080);
    private static readonly ProbeRect SideBounds = new(-1080, 0, 0, 1920);

    [TestMethod]
    public void Capture_RequiresStableActiveDesktopAndPerMonitorCoordinates()
    {
        var report = Report([Window(Desktop, true)]);
        Assert.ThrowsExactly<InvalidOperationException>(() => DesktopSnapshotCapture.FromReport(Other, report));
        Assert.ThrowsExactly<InvalidOperationException>(() => DesktopSnapshotCapture.FromReport(Desktop, report with { TopologyStable = false }));
        Assert.ThrowsExactly<InvalidOperationException>(() => DesktopSnapshotCapture.FromReport(Desktop, report with { DesktopHintStable = false }));
        Assert.ThrowsExactly<InvalidOperationException>(() => DesktopSnapshotCapture.FromReport(Desktop, report with { PerMonitorDpiContext = false }));
        Assert.ThrowsExactly<InvalidOperationException>(() => DesktopSnapshotCapture.FromReport(Desktop, report with { IncludesIdentities = false }));
    }

    [TestMethod]
    public void Capture_ExcludesOtherDesktopAndUnverifiedWindowsAndKeepsSeparateInstances()
    {
        var first = Window(Desktop, true);
        var spanning = first with
        {
            Handle = "0x2", Title = "Second", EnumerationIndex = 1,
            ScreenBounds = new ProbeRect(-200, 50, 400, 600),
            NormalPlacementBounds = new ProbeRect(-200, 40, 400, 590)
        };
        var report = Report([
            first, spanning,
            first with { Membership = new ProbeDesktopMembership(Other, 0, false, 0) },
            first with { Membership = new ProbeDesktopMembership(Other, 0, true, 0) },
            first with { Membership = new ProbeDesktopMembership(Desktop, 0, false, 0) },
            first with { Classification = "branchwatch" },
            first with { Classification = "auxiliary" },
            first with { NormalPlacementBounds = null },
            first with { Membership = new ProbeDesktopMembership(Desktop, -1, true, 0) }
        ]);
        var snapshot = DesktopSnapshotCapture.FromReport(Desktop, report);

        Assert.HasCount(2, snapshot.Windows);
        Assert.AreNotEqual(snapshot.Windows[0].Id, snapshot.Windows[1].Id);
        Assert.AreEqual(4, snapshot.ExcludedWindowCount);
        CollectionAssert.AreEquivalent(new[] { "main", "side" }, snapshot.Windows[1].IntersectingMonitors.ToArray());
        Assert.AreEqual(spanning.NormalPlacementBounds!.Value, snapshot.Windows[1].NormalPlacementBounds);
        Assert.AreEqual(spanning.ScreenBounds!.Value, snapshot.Windows[1].ScreenBounds);
        Assert.AreEqual<int?>(90, snapshot.Monitors[1].RotationDegrees);
        var serialized = JsonSerializer.Serialize(snapshot);
        Assert.IsFalse(serialized.Contains("\"Handle\"", StringComparison.Ordinal));
        Assert.IsFalse(serialized.Contains("\"ProcessId\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Store_RoundTripsIndependentSnapshotsAndRejectsStaleOrEmptyReplacement()
    {
        InDirectory(directory =>
        {
            var path = Path.Combine(directory, "snapshots.json");
            var store = new DesktopMemoryStore(path);
            var first = Snapshot();
            var other = first with { DesktopId = Other, DesktopName = "Other" };
            store.Save(first, null);
            store.Save(other, null);
            var original = File.ReadAllText(path);
            Assert.HasCount(2, store.Load());
            Assert.ThrowsExactly<InvalidOperationException>(() => store.Save(first with { CapturedUtc = Captured.AddMinutes(1) }, null));
            Assert.ThrowsExactly<InvalidOperationException>(() => store.Save(first with { Windows = [] }, Captured));
            Assert.AreEqual(original, File.ReadAllText(path));

            var replacement = first with { CapturedUtc = Captured.AddMinutes(1) };
            store.Save(replacement, Captured);
            var restored = new DesktopMemoryStore(path).Load();
            Assert.AreEqual(replacement.CapturedUtc, restored.Single(s => s.DesktopId == Desktop).CapturedUtc);
            Assert.AreEqual(first.Windows[0].Title, restored.Single(s => s.DesktopId == Desktop).Windows[0].Title);
            Assert.AreEqual(first.Monitors[1].Bounds, restored.Single(s => s.DesktopId == Desktop).Monitors[1].Bounds);
            Assert.AreEqual(original, File.ReadAllText(Path.Combine(directory, "snapshots.backup.json")));
            Assert.ThrowsExactly<InvalidOperationException>(() => store.Delete(Desktop, Captured));
            store.Delete(Desktop, replacement.CapturedUtc);
            Assert.AreEqual(Other, store.Load().Single().DesktopId);
        });
    }

    [TestMethod]
    public void Store_CorruptOrUnsupportedFileIsNeverOverwritten()
    {
        InDirectory(directory =>
        {
            var path = Path.Combine(directory, "snapshots.json");
            var store = new DesktopMemoryStore(path);
            File.WriteAllText(path, "{");
            try
            {
                store.Save(Snapshot(), null);
                Assert.Fail("Corrupt snapshot storage was accepted.");
            }
            catch (JsonException) { }
            Assert.AreEqual("{", File.ReadAllText(path));
            const string unsupported = "{\"SchemaVersion\":99,\"Snapshots\":[]}";
            File.WriteAllText(path, unsupported);
            Assert.ThrowsExactly<InvalidDataException>(() => store.Save(Snapshot(), null));
            Assert.AreEqual(unsupported, File.ReadAllText(path));
        });
    }

    private static DesktopSnapshot Snapshot() => DesktopSnapshotCapture.FromReport(Desktop, Report([Window(Desktop, true)]));

    private static DesktopMemoryProbeReport Report(IReadOnlyList<ProbeWindow> windows)
    {
        var state = new ProbeDesktopState(Desktop, [new ProbeDesktop(Desktop, "Work"), new ProbeDesktop(Other, "Other")]);
        ProbeMonitor[] monitors =
        [
            new("main", MainBounds, MainBounds, true, 0, 1920, 1080, 60, 100, 0, [new ProbeDisplayDevice("main-id", 1, "Main")]),
            new("side", SideBounds, SideBounds, false, 90, 1080, 1920, 60, 125, 0, [new ProbeDisplayDevice("side-id", 1, "Side")])
        ];
        return new DesktopMemoryProbeReport(1, Captured.AddSeconds(-1), Captured, "Windows", "X64", true, true,
            state, state, monitors, monitors, true, true, windows, []);
    }

    private static ProbeWindow Window(Guid desktop, bool current) => new(
        Handle: "0x1", ProcessId: 1, ProcessName: "Editor", ExecutablePath: @"C:\Apps\Editor.exe",
        Title: "First", ClassName: "EditorWindow", Visible: true, Owned: false, ToolWindow: false,
        AppWindow: true, Minimized: false, Maximized: false, CloakFlags: 0, CloakHResult: 0, StyleError: null,
        ScreenBounds: new ProbeRect(50, 50, 700, 600), BoundsError: null, VisibleFrameBounds: null, FrameHResult: 0,
        NormalPlacementBounds: new ProbeRect(50, 40, 700, 590), PlacementError: null, ShowCommand: 1, PlacementFlags: 0,
        MonitorSource: "main", WindowDpi: 96, Membership: new ProbeDesktopMembership(desktop, 0, current, 0),
        Classification: "candidate", EnumerationIndex: 0);

    private static void InDirectory(Action<string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, recursive: true); }
    }
}

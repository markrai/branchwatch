using System.IO;
using System.Text.Json;

namespace BranchWatch.Tests;

[TestClass]
public sealed class ApplicationRestoreAdapterTests
{
    [TestMethod]
    public void Build_UsesFixedArgumentsWithoutShellInterpolation()
    {
        var folder = @"C:\Projects\One & Two";
        var code = WithFolder(@"C:\Apps\Code.exe", "Chrome_WidgetWin_1", ApplicationFolderKind.VisualStudioCode, folder);
        var cursor = WithFolder(@"C:\Apps\Cursor.exe", "Chrome_WidgetWin_1", ApplicationFolderKind.Cursor, folder);
        foreach (var window in new[] { code, cursor })
        {
            var spec = ApplicationRestoreAdapters.Build(window);
            Assert.IsFalse(spec.OpenOnly);
            CollectionAssert.AreEqual(new[] { "--new-window", folder }, spec.Arguments.ToArray());
            var start = NativeMissingApplicationLaunchEnvironment.CreateStartInfo(spec);
            Assert.IsFalse(start.UseShellExecute);
            Assert.AreEqual(window.ExecutablePath, start.FileName);
            Assert.AreEqual(@"C:\Apps", start.WorkingDirectory);
            CollectionAssert.AreEqual(spec.Arguments.ToArray(), start.ArgumentList.ToArray());
        }

        var explorer = ApplicationRestoreAdapters.Build(WithFolder(@"C:\Windows\explorer.exe", "CabinetWClass",
            ApplicationFolderKind.Explorer, folder));
        Assert.IsTrue(explorer.OpenOnly);
        CollectionAssert.AreEqual(new[] { folder }, explorer.Arguments.ToArray());
        var terminal = ApplicationRestoreAdapters.Build(WithFolder(@"C:\Apps\WindowsTerminal.exe", "CASCADIA_HOSTING_WINDOW_CLASS",
            ApplicationFolderKind.WindowsTerminal, folder));
        Assert.IsTrue(terminal.OpenOnly);
        CollectionAssert.AreEqual(new[] { "-w", "new", "new-tab", "-d", folder }, terminal.Arguments.ToArray());
    }

    [TestMethod]
    public void Validate_RejectsWrongApplicationAndNoncanonicalOrSpecialPaths()
    {
        var terminal = WithFolder(@"C:\Apps\WindowsTerminal.exe", "CASCADIA_HOSTING_WINDOW_CLASS",
            ApplicationFolderKind.WindowsTerminal, @"C:\Projects\One");
        var explorer = WithFolder(@"C:\Windows\explorer.exe", "CabinetWClass", ApplicationFolderKind.Explorer, @"C:\Projects\One");
        foreach (var path in new[] { @"relative", @"C:relative", @"\\server\share", @"C:\Projects\..\Other", @"C:\Projects\One:stream", "C:\\Projects\\\"quoted\"" })
            Assert.IsFalse(ApplicationRestoreAdapters.IsCanonicalFolder(path), path);
        Assert.IsFalse(ApplicationRestoreAdapters.IsValidContext(terminal, new(ApplicationFolderKind.WindowsTerminal, @"C:\One;Two")));
        Assert.IsFalse(ApplicationRestoreAdapters.IsValidContext(explorer, new(ApplicationFolderKind.Explorer, @"C:\One,Two")));
        Assert.IsFalse(ApplicationRestoreAdapters.IsValidContext(terminal, new(ApplicationFolderKind.Cursor, @"C:\One")));
        Assert.IsNull(ApplicationRestoreAdapters.Detect(terminal with { ClassName = "WrongClass" }));
        Assert.ThrowsExactly<InvalidOperationException>(() => ApplicationRestoreAdapters.Build(terminal with
        {
            FolderContext = new(ApplicationFolderKind.Cursor, @"C:\One")
        }));
    }

    [TestMethod]
    public void Snapshot_OptionalFolderContextRoundTripsAndOldSnapshotRemainsReadable()
    {
        var window = WithFolder(@"C:\Apps\Code.exe", "Chrome_WidgetWin_1", ApplicationFolderKind.VisualStudioCode, @"C:\Projects\One");
        var data = JsonSerializer.Serialize(window);
        var restored = JsonSerializer.Deserialize<SnapshotWindow>(data)!;
        Assert.AreEqual(window.FolderContext, restored.FolderContext);
        var old = System.Text.Json.Nodes.JsonNode.Parse(data)!.AsObject();
        old.Remove("FolderContext");
        var oldWindow = JsonSerializer.Deserialize<SnapshotWindow>(old.ToJsonString())!;
        Assert.IsNull(oldWindow.FolderContext);
        Assert.HasCount(0, ApplicationRestoreAdapters.Build(oldWindow).Arguments);
        var snapshot = Snapshot(window);
        DesktopSnapshotValidation.Validate(snapshot);
        Assert.ThrowsExactly<InvalidDataException>(() => DesktopSnapshotValidation.Validate(snapshot with
        {
            Windows = [window with { FolderContext = new(ApplicationFolderKind.Explorer, @"C:\Projects\One") }]
        }));
    }

    [TestMethod]
    public void CheckFolder_MissingFolderIsBlocked()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var spec = ApplicationRestoreAdapters.Build(WithFolder(@"C:\Apps\Code.exe", "Chrome_WidgetWin_1",
            ApplicationFolderKind.VisualStudioCode, folder));
        Assert.IsNotNull(ApplicationRestoreAdapters.CheckFolder(spec));
    }

    private static SnapshotWindow WithFolder(string path, string className, ApplicationFolderKind kind, string folder)
    {
        var bounds = new ProbeRect(100, 100, 700, 600);
        return new SnapshotWindow(Guid.NewGuid(), "App", path, "Project", className, bounds, null, bounds,
            false, false, 1, 0, "main", 96, 0, ["main"]) { FolderContext = new(kind, folder) };
    }

    private static DesktopSnapshot Snapshot(SnapshotWindow window)
    {
        var screen = new ProbeRect(0, 0, 1920, 1080);
        return new(Guid.NewGuid(), "Work", DateTimeOffset.UtcNow,
            [new SnapshotMonitor("main", screen, screen, true, 0, 1920, 1080, 60, 100, [])], [window], 0, []);
    }
}

using System.IO;
using System.Text.Json;

namespace BranchWatch.Tests;

[TestClass]
public sealed class BrowserTabCaptureTests
{
    private const string Profile = "0123456789abcdef0123456789abcdef";
    private const string Nonce = "fedcba9876543210fedcba9876543210";
    private static readonly BrowserNativeToken Token = new("0x123", 42, "123456789");
    private static readonly BrowserSavedTab[] Tabs = [new("https://example.com/path?q=one#two", true, false), new("https://example.com/path?q=one#two", false, true)];

    [TestMethod]
    public void Model_ValidatesHttpUrlsAndPreservesDuplicateOrder()
    {
        Assert.IsTrue(BrowserTabCapture.IsValid(new("chrome", Profile, Tabs)));
        foreach (var url in new[] { "file:///C:/secret", "javascript:alert(1)", "chrome://settings", "https://user:pass@example.com/", "relative", "https://example.com/a b", "https://example.com/" + new string('a', 2048) })
            Assert.IsFalse(BrowserTabCapture.IsUrl(url), url);
        Assert.IsTrue(BrowserTabCapture.IsValid(new("edge", Profile, [])));
        Assert.IsFalse(BrowserTabCapture.IsValid(new("edge", Profile, [Tabs[1], Tabs[1]])));
        Assert.IsFalse(BrowserTabCapture.IsValid(new("edge", Profile, Enumerable.Repeat(Tabs[0], 101).ToArray())));
    }

    [TestMethod]
    public void Protocol_RejectsUnexpectedFieldsAndUnsafeUrls()
    {
        var request = new BrowserCaptureRequest(1, "capture", Profile, Nonce, Profile, 7, Tabs, 0);
        var bytes = BrowserCompanionProtocol.Encode(request);
        Assert.ThrowsExactly<InvalidDataException>(() => BrowserTabCapture.ParseRequest(BrowserCompanionProtocol.Encode(request with { Tabs = [] })));
        var parsed = BrowserTabCapture.ParseRequest(bytes);
        CollectionAssert.AreEqual(Tabs, parsed.Tabs.ToArray());
        var json = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.ThrowsExactly<InvalidDataException>(() => BrowserTabCapture.ParseRequest(System.Text.Encoding.UTF8.GetBytes(json.Replace("\"version\":1", "\"version\":1,\"version\":1"))));
        Assert.ThrowsExactly<InvalidDataException>(() => BrowserTabCapture.ParseRequest(BrowserCompanionProtocol.Encode(request with { Tabs = [new("file:///C:/secret", false, true)] })));
        var desktop = Guid.NewGuid();
        var envelope = new BrowserCaptureEnvelope(request, "edge", Token, desktop);
        var decoded = BrowserTabCapture.ParseEnvelope(BrowserCompanionProtocol.Encode(envelope));
        Assert.AreEqual(Token, decoded.Token);
        Assert.AreEqual(desktop, decoded.DesktopId);
        Assert.ThrowsExactly<InvalidDataException>(() => BrowserTabCapture.ParseEnvelope(BrowserCompanionProtocol.Encode(envelope with { Token = Token with { ProcessCreation = "0" } })));
    }

    [TestMethod]
    public void Inbox_RejectsWrongWindowDesktopAndReplay()
    {
        var inbox = new BrowserCaptureInbox();
        var desktop = Guid.NewGuid();
        var ticket = inbox.Arm(desktop, Guid.NewGuid(), "chrome", Token);
        var request = new BrowserCaptureRequest(1, "capture", Profile, Nonce, ticket.Code, 7, Tabs, 0);
        var envelope = new BrowserCaptureEnvelope(request, "chrome", Token, desktop);
        try
        {
            Assert.IsFalse(inbox.TryReceive(envelope with { DesktopId = Guid.NewGuid() }));
            Assert.IsFalse(inbox.TryReceive(envelope with { Browser = "edge" }));
            Assert.IsFalse(inbox.TryReceive(envelope with { Token = Token with { Handle = "0x999" } }));
            Assert.IsFalse(inbox.TryReceive(envelope with { Request = request with { Ticket = Profile } }));
            Assert.IsTrue(inbox.TryReceive(envelope));
            Assert.IsFalse(inbox.TryReceive(envelope));
            Assert.IsNull(inbox.Take(Profile));
            Assert.AreEqual(envelope, inbox.Take(ticket.Code));
            Assert.IsNull(inbox.Take(ticket.Code));
            Assert.IsFalse(inbox.TryReceive(envelope));
        }
        finally { inbox.Cancel(ticket.Code); }
    }

    [TestMethod]
    public void Inbox_ReplacingOrCancellingTicketDiscardsReceivedTabs()
    {
        var inbox = new BrowserCaptureInbox();
        var desktop = Guid.NewGuid();
        var first = inbox.Arm(desktop, Guid.NewGuid(), "edge", Token);
        var envelope = new BrowserCaptureEnvelope(new(1, "capture", Profile, Nonce, first.Code, 7, Tabs, 0), "edge", Token, desktop);
        Assert.IsTrue(inbox.TryReceive(envelope));
        var second = inbox.Arm(desktop, Guid.NewGuid(), "edge", Token);
        try
        {
            Assert.IsNull(inbox.Take(first.Code));
            Assert.IsFalse(inbox.TryReceive(envelope));
            Assert.IsTrue(inbox.TryReceive(envelope with { Request = envelope.Request with { Ticket = second.Code } }));
            inbox.Cancel(second.Code);
            Assert.IsNull(inbox.Take(second.Code));
        }
        finally { inbox.Cancel(second.Code); }
    }

    [TestMethod]
    public void Snapshot_RoundTripsTabContextAndReadsOldWindows()
    {
        var bounds = new ProbeRect(10, 10, 800, 600);
        var window = new SnapshotWindow(Guid.NewGuid(), "chrome", @"C:\Apps\chrome.exe", "Window",
            "Chrome_WidgetWin_1", bounds, null, bounds, false, false, 1, 0, "main", 96, 0, ["main"])
            { BrowserContext = new("chrome", Profile, Tabs) };
        var screen = new ProbeRect(0, 0, 1920, 1080);
        var snapshot = new DesktopSnapshot(Guid.NewGuid(), "Work", DateTimeOffset.UtcNow,
            [new SnapshotMonitor("main", screen, screen, true, 0, 1920, 1080, 60, 100, [])], [window], 0, []);
        DesktopSnapshotValidation.Validate(snapshot);
        Assert.ThrowsExactly<InvalidDataException>(() => DesktopSnapshotValidation.Validate(snapshot with
        {
            Windows = [window with { BrowserContext = new("edge", Profile, Tabs) }]
        }));
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(folder, "snapshots.json");
            var store = new DesktopMemoryStore(path);
            store.Save(snapshot, null);
            var restored = store.Load().Single().Windows.Single();
            Assert.AreEqual(Profile, restored.BrowserContext!.ProfileId);
            CollectionAssert.AreEqual(Tabs, restored.BrowserContext.Tabs.ToArray());
            Assert.IsFalse(File.ReadAllText(path).Contains("ProcessCreation", StringComparison.Ordinal));
            var old = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(window))!.AsObject();
            old.Remove("BrowserContext");
            Assert.IsNull(JsonSerializer.Deserialize<SnapshotWindow>(old.ToJsonString())!.BrowserContext);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [TestMethod]
    public void Firefox_SavedDataRoundTripsButLiveOperationsRemainLocked()
    {
        var context = new BrowserWindowContext("firefox", Profile, Tabs);
        Assert.IsTrue(BrowserTabCapture.IsValid(context));
        Assert.IsTrue(BrowserRestoreProtocol.HasValidRestoreTabs(context));
        Assert.IsFalse(BrowserRestoreProtocol.CanRestore(context));
        Assert.IsFalse(BrowserTabCapture.SupportsLiveSession("firefox"));
        Assert.IsNull(BrowserTabCapture.BrowserFor(@"C:\Apps\firefox.exe", "MozillaWindowClass"));
        Assert.AreEqual("firefox", BrowserTabCapture.SavedBrowserFor(@"C:\Apps\firefox.exe", "MozillaWindowClass"));
        Assert.IsNull(BrowserTabCapture.SavedBrowserFor(@"C:\Apps\firefox.exe", "Chrome_WidgetWin_1"));
        var desktop = Guid.NewGuid();
        var id = Guid.NewGuid();
        var request = new BrowserCaptureRequest(1, "capture", Profile, Nonce, Profile, 7, Tabs, 0);
        var envelope = new BrowserCaptureEnvelope(request, "firefox", Token, desktop);
        Assert.ThrowsExactly<InvalidDataException>(() => BrowserTabCapture.ParseEnvelope(BrowserCompanionProtocol.Encode(envelope)));
        var inbox = new BrowserCaptureInbox();
        Assert.ThrowsExactly<InvalidOperationException>(() => inbox.Arm(desktop, id, "firefox", Token));
        Assert.IsFalse(inbox.TryReceive(envelope));
        var key = new BrowserRestoreKey(desktop, DateTimeOffset.UtcNow, id);
        Assert.ThrowsExactly<InvalidOperationException>(() => new BrowserRestoreQueue().Arm(key, context, () => true));
        var restore = new BrowserRestoreEnvelope(new(2, "restore", Profile, Nonce, Profile, 7), "firefox", desktop);
        Assert.ThrowsExactly<InvalidDataException>(() => BrowserRestoreProtocol.ParseEnvelope(BrowserCompanionProtocol.Encode(restore)));

        var bounds = new ProbeRect(10, 10, 800, 600);
        var window = new SnapshotWindow(id, "firefox", @"C:\Apps\firefox.exe", "Window", "MozillaWindowClass",
            bounds, null, bounds, false, false, 1, 0, "main", 96, 0, ["main"]) { BrowserContext = context };
        var screen = new ProbeRect(0, 0, 1920, 1080);
        var snapshot = new DesktopSnapshot(desktop, "Work", key.CapturedUtc,
            [new SnapshotMonitor("main", screen, screen, true, 0, 1920, 1080, 60, 100, [])], [window], 0, []);
        Assert.ThrowsExactly<InvalidDataException>(() => DesktopSnapshotValidation.Validate(snapshot with
        {
            Windows = [window with { BrowserContext = context with { Browser = "chrome" } }]
        }));
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new DesktopMemoryStore(Path.Combine(folder, "snapshots.json"));
            store.Save(snapshot, null);
            var saved = store.Load().Single().Windows.Single().BrowserContext!;
            Assert.AreEqual("firefox", saved.Browser);
            Assert.AreEqual(Profile, saved.ProfileId);
            CollectionAssert.AreEqual(Tabs, saved.Tabs.ToArray());
            Assert.IsFalse(BrowserRestoreProtocol.CanRestore(saved));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}

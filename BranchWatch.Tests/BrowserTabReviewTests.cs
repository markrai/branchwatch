using System.IO;

namespace BranchWatch.Tests;

[TestClass]
public sealed class BrowserTabReviewTests
{
    private const string Profile = "0123456789abcdef0123456789abcdef";
    private const string OtherProfile = "fedcba9876543210fedcba9876543210";
    private static readonly BrowserSavedTab[] Tabs =
    [
        new("https://example.com/duplicate", true, false),
        new("https://example.com/private?token=do-not-save", false, true),
        new("https://example.com/duplicate", false, false),
        new("https://example.com/last", false, false)
    ];

    private static SnapshotWindow Window(string browser, string profile, IReadOnlyList<BrowserSavedTab> tabs, int index)
    {
        var bounds = new ProbeRect(10, 10, 800, 600);
        var exe = browser == "chrome" ? "chrome.exe" : "msedge.exe";
        return new SnapshotWindow(Guid.NewGuid(), exe, @"C:\Apps\" + exe, "Window",
            "Chrome_WidgetWin_1", bounds, null, bounds, false, false, 1, 0, "main", 96, index, ["main"])
        {
            BrowserContext = new(browser, profile, tabs)
        };
    }

    private static DesktopSnapshot Draft(params SnapshotWindow[] windows)
    {
        var bounds = new ProbeRect(0, 0, 1920, 1080);
        return new(Guid.NewGuid(), "Work", DateTimeOffset.UtcNow,
            [new SnapshotMonitor("main", bounds, bounds, true, 0, 1920, 1080, 60, 100, [])], windows, 0, []);
    }

    [TestMethod]
    public void Selection_PreservesOrderMetadataAndIndependentDuplicates()
    {
        var window = Window("chrome", Profile, Tabs, 0);
        var draft = Draft(window);
        var review = new BrowserTabReview();
        review.Synchronize(draft);
        Assert.AreEqual(4, review.SelectedCount(window.Id));
        review.Set(window.Id, 0, false);
        review.Set(window.Id, 1, false);
        var saved = review.Apply(draft).Windows.Single().BrowserContext!;
        CollectionAssert.AreEqual(new[] { Tabs[2], Tabs[3] }, saved.Tabs.ToArray());
        Assert.IsFalse(saved.Tabs.Any(t => t.Active));
        Assert.AreEqual(4, draft.Windows.Single().BrowserContext!.Tabs.Count);
        Assert.AreEqual(Profile, saved.ProfileId);
    }

    [TestMethod]
    public void Selection_SurvivesRefreshAndOtherWindowImportButResetsRecapturedWindow()
    {
        var first = Window("chrome", Profile, Tabs, 0);
        var second = Window("edge", OtherProfile, Tabs, 1);
        var draft = Draft(first);
        var review = new BrowserTabReview();
        review.Synchronize(draft);
        review.Set(first.Id, 1, false);
        review.Synchronize(draft with { Windows = [first with { Title = "Same context" }] });
        Assert.IsFalse(review.IsSelected(first.Id, 1));
        var expanded = draft with { Windows = [first, second] };
        review.Synchronize(expanded);
        Assert.IsFalse(review.IsSelected(first.Id, 1));
        Assert.AreEqual(4, review.SelectedCount(second.Id));
        review.SetAll(second.Id, false);
        var recaptured = first with { BrowserContext = new("chrome", Profile, Tabs.ToArray()) };
        review.Synchronize(expanded with { Windows = [recaptured, second] });
        Assert.AreEqual(4, review.SelectedCount(first.Id));
        Assert.AreEqual(0, review.SelectedCount(second.Id));
    }

    [TestMethod]
    public void Selection_RejectsUnreviewedOrStaleContextAndClearResetsDraft()
    {
        var window = Window("chrome", Profile, Tabs, 0);
        var draft = Draft(window);
        var review = new BrowserTabReview();
        Assert.ThrowsExactly<InvalidOperationException>(() => review.Apply(draft));
        review.Synchronize(draft);
        review.SetAll(window.Id, false);
        var changed = draft with { Windows = [window with { BrowserContext = new("chrome", Profile, [Tabs[0]]) }] };
        Assert.ThrowsExactly<InvalidOperationException>(() => review.Apply(changed));
        review.Clear();
        Assert.ThrowsExactly<InvalidOperationException>(() => review.Apply(draft));
        review.Synchronize(draft);
        Assert.AreEqual(4, review.SelectedCount(window.Id));
    }

    [TestMethod]
    public void Persistence_OnlySelectedUrlsAreWrittenAndEmptyContextRemainsExplicit()
    {
        var first = Window("chrome", Profile, Tabs, 0);
        var second = Window("edge", OtherProfile, Tabs, 1);
        var draft = Draft(first, second);
        var review = new BrowserTabReview();
        review.Synchronize(draft);
        review.Set(first.Id, 1, false);
        review.SetAll(second.Id, false);
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(folder, "snapshots.json");
            var store = new DesktopMemoryStore(path);
            store.Save(review.Apply(draft), null);
            Assert.IsFalse(File.ReadAllText(path).Contains("do-not-save", StringComparison.Ordinal));
            var saved = store.Load().Single();
            CollectionAssert.AreEqual(new[] { Tabs[0], Tabs[2], Tabs[3] }, saved.Windows[0].BrowserContext!.Tabs.ToArray());
            Assert.AreEqual(OtherProfile, saved.Windows[1].BrowserContext!.ProfileId);
            Assert.AreEqual(0, saved.Windows[1].BrowserContext!.Tabs.Count);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}

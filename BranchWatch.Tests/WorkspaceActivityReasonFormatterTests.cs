namespace BranchWatch.Tests;

[TestClass]
public sealed class WorkspaceActivityReasonFormatterTests
{
    [TestMethod]
    public void ShowWorkspaceActivityReasonDefaultsToFalse()
    {
        var settings = new AppSettings();

        Assert.IsFalse(settings.ShowWorkspaceActivityReason);
    }

    [TestMethod]
    public void OverlayActivityReasonIsHiddenByDefault()
    {
        var settings = new AppSettings
        {
            WatchMode = RepositoryWatchMode.WorkspaceRepo
        };

        var reason = WorkspaceActivityReasonFormatter.FormatForOverlay(settings, WorkspaceActivityReason.FileChanged);

        Assert.IsNull(reason);
    }

    [TestMethod]
    public void OverlayActivityReasonAppearsWhenEnabledInWorkspaceRepoMode()
    {
        var settings = new AppSettings
        {
            WatchMode = RepositoryWatchMode.WorkspaceRepo,
            ShowWorkspaceActivityReason = true
        };

        var reason = WorkspaceActivityReasonFormatter.FormatForOverlay(settings, WorkspaceActivityReason.IndexChanged);

        Assert.AreEqual("index changed", reason);
    }

    [TestMethod]
    public void OverlayActivityReasonIsHiddenInPinnedRepoMode()
    {
        var settings = new AppSettings
        {
            WatchMode = RepositoryWatchMode.PinnedRepo,
            ShowWorkspaceActivityReason = true
        };

        var reason = WorkspaceActivityReasonFormatter.FormatForOverlay(settings, WorkspaceActivityReason.FileChanged);

        Assert.IsNull(reason);
    }

    [TestMethod]
    public void FormatterIncludesRepoOpenedLabel()
    {
        var reason = WorkspaceActivityReasonFormatter.Format(WorkspaceActivityReason.RepoOpened);

        Assert.AreEqual("repo opened", reason);
    }
}

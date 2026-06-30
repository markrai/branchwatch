namespace BranchWatch.Tests;

[TestClass]
public sealed class WorkspaceRepositoryControllerTests
{
    [TestMethod]
    public void PinnedRepoBehaviorRemainsUnchanged()
    {
        using var workspace = new TestRepositoryWorkspace();
        var repo = workspace.CreateRepo("repo");
        using var watcher = new GitRepositoryWatcher();
        var settings = CreateSettings(RepositoryWatchMode.PinnedRepo, pinnedRepository: repo);
        using var controller = CreateController(workspace, settings, watcher);

        var result = controller.LoadPinnedRepository();

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.AreEqual(repo, watcher.CurrentStatus.RepositoryRoot);
        Assert.AreEqual(RepositoryWatchMode.PinnedRepo, settings.WatchMode);
    }

    [TestMethod]
    public void WorkspaceModeStartsWithPinnedRepoWhenPinnedRepoIsDiscovered()
    {
        using var workspace = new TestRepositoryWorkspace();
        var repo1 = workspace.CreateRepo("repo1");
        var repo2 = workspace.CreateRepo("repo2");
        using var watcher = new GitRepositoryWatcher();
        var settings = CreateSettings(RepositoryWatchMode.WorkspaceRepo, pinnedRepository: repo2, workspaceRoot: workspace.Root);
        using var controller = CreateController(workspace, settings, watcher);

        var result = controller.LoadWorkspace();

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.AreEqual(repo2, watcher.CurrentStatus.RepositoryRoot);
        Assert.AreEqual(repo2, settings.WatchedRepositoryPath);
        Assert.HasCount(2, result.Repositories);
        Assert.IsTrue(result.Repositories.Any(repository => repository.RepositoryRoot == repo1));
    }

    [TestMethod]
    public void WorkspaceModeWaitsForActivityWhenPinnedRepoIsOutsideWorkspace()
    {
        using var workspace = new TestRepositoryWorkspace();
        workspace.CreateRepo("repo1");
        workspace.CreateRepo("repo2");
        var outsideRepo = Path.Combine(Path.GetTempPath(), "BranchWatchOutside", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(outsideRepo, ".git"));
        using var watcher = new GitRepositoryWatcher();
        var settings = CreateSettings(RepositoryWatchMode.WorkspaceRepo, pinnedRepository: outsideRepo, workspaceRoot: workspace.Root);
        using var controller = CreateController(workspace, settings, watcher);

        try
        {
            var result = controller.LoadWorkspace();

            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.IsNull(watcher.CurrentStatus.RepositoryRoot);
            Assert.AreEqual(RepositorySessionController.WaitingForWorkspaceActivityStatus, controller.WorkspaceStatusText);
            Assert.AreEqual(outsideRepo, settings.WatchedRepositoryPath);
        }
        finally
        {
            Directory.Delete(outsideRepo, recursive: true);
        }
    }

    [TestMethod]
    public void WorkspaceModeShowsStatusWhenNoReposAreFound()
    {
        using var workspace = new TestRepositoryWorkspace();
        workspace.CreatePlainDirectory("plain");
        using var watcher = new GitRepositoryWatcher();
        var settings = CreateSettings(RepositoryWatchMode.WorkspaceRepo, workspaceRoot: workspace.Root);
        using var controller = CreateController(workspace, settings, watcher);

        var result = controller.LoadWorkspace();

        Assert.IsTrue(result.Success);
        Assert.AreEqual(RepositorySessionController.NoRepositoriesFoundStatus, result.ErrorMessage);
        Assert.AreEqual(RepositorySessionController.NoRepositoriesFoundStatus, controller.WorkspaceStatusText);
        Assert.IsNull(watcher.CurrentStatus.RepositoryRoot);
    }

    [TestMethod]
    public void WorkspaceModePromotesRepositoryOnBranchChange()
    {
        using var workspace = new TestRepositoryWorkspace();
        var repo1 = workspace.CreateRepo("repo1", "main");
        var repo2 = workspace.CreateRepo("repo2", "main");
        using var watcher = new GitRepositoryWatcher();
        var settings = CreateSettings(RepositoryWatchMode.WorkspaceRepo, pinnedRepository: repo1, workspaceRoot: workspace.Root);
        using var controller = CreateController(workspace, settings, watcher);

        Assert.IsTrue(controller.LoadWorkspace().Success);
        workspace.SetBranch(repo2, "dev");
        var promoted = controller.RefreshWorkspaceRepository(repo2);

        Assert.IsTrue(promoted);
        Assert.AreEqual(repo2, watcher.CurrentStatus.RepositoryRoot);
        Assert.AreEqual("dev", watcher.CurrentStatus.BranchDisplay);
        Assert.AreEqual(WorkspaceActivityReason.BranchChanged, controller.LastWorkspaceActivityReason);
        Assert.AreEqual(repo1, settings.WatchedRepositoryPath);
    }

    [TestMethod]
    public void WorkspaceModePromotesInactiveRepositoryOnFileChange()
    {
        using var workspace = new TestRepositoryWorkspace();
        var repo1 = workspace.CreateRepo("repo1", "main");
        var repo2 = workspace.CreateRepo("repo2", "master");
        using var watcher = new GitRepositoryWatcher();
        var settings = CreateSettings(RepositoryWatchMode.WorkspaceRepo, pinnedRepository: repo1, workspaceRoot: workspace.Root);
        using var controller = CreateController(workspace, settings, watcher);

        Assert.IsTrue(controller.LoadWorkspace().Success);
        var promoted = controller.PromoteWorkspaceRepositoryForFileActivity(repo2, Path.Combine(repo2, "src", "Program.cs"));

        Assert.IsTrue(promoted);
        Assert.AreEqual(repo2, watcher.CurrentStatus.RepositoryRoot);
        Assert.AreEqual("master", watcher.CurrentStatus.BranchDisplay);
        Assert.AreEqual(WorkspaceActivityReason.FileChanged, controller.LastWorkspaceActivityReason);
    }

    [TestMethod]
    public void WorkspaceModeRefreshesActiveRepositoryOnFileChange()
    {
        using var workspace = new TestRepositoryWorkspace();
        var repo = workspace.CreateRepo("repo", "main");
        using var watcher = new GitRepositoryWatcher();
        var settings = CreateSettings(RepositoryWatchMode.WorkspaceRepo, pinnedRepository: repo, workspaceRoot: workspace.Root);
        using var controller = CreateController(workspace, settings, watcher);

        Assert.IsTrue(controller.LoadWorkspace().Success);
        workspace.SetBranch(repo, "dev");
        var promoted = controller.PromoteWorkspaceRepositoryForFileActivity(repo, Path.Combine(repo, "src", "Program.cs"));

        Assert.IsTrue(promoted);
        Assert.AreEqual(repo, watcher.CurrentStatus.RepositoryRoot);
        Assert.AreEqual("dev", watcher.CurrentStatus.BranchDisplay);
        Assert.AreEqual(WorkspaceActivityReason.FileChanged, controller.LastWorkspaceActivityReason);
    }

    [TestMethod]
    public void WorkspaceModeDoesNotPromoteGitMetadataAsFileActivity()
    {
        using var workspace = new TestRepositoryWorkspace();
        var repo1 = workspace.CreateRepo("repo1", "main");
        var repo2 = workspace.CreateRepo("repo2", "main");
        using var watcher = new GitRepositoryWatcher();
        var settings = CreateSettings(RepositoryWatchMode.WorkspaceRepo, pinnedRepository: repo1, workspaceRoot: workspace.Root);
        using var controller = CreateController(workspace, settings, watcher);

        Assert.IsTrue(controller.LoadWorkspace().Success);
        var promoted = controller.PromoteWorkspaceRepositoryForFileActivity(repo2, Path.Combine(repo2, ".git", "HEAD"));

        Assert.IsFalse(promoted);
        Assert.AreEqual(repo1, watcher.CurrentStatus.RepositoryRoot);
        Assert.AreEqual(WorkspaceActivityReason.WorkspaceLoaded, controller.LastWorkspaceActivityReason);
    }

    [TestMethod]
    public void WorkspaceModePromotesRepositoryOnIndexChange()
    {
        using var workspace = new TestRepositoryWorkspace();
        var repo1 = workspace.CreateRepo("repo1", "main");
        var repo2 = workspace.CreateRepo("repo2", "main");
        using var watcher = new GitRepositoryWatcher();
        var settings = CreateSettings(RepositoryWatchMode.WorkspaceRepo, pinnedRepository: repo1, workspaceRoot: workspace.Root);
        using var controller = CreateController(workspace, settings, watcher);

        Assert.IsTrue(controller.LoadWorkspace().Success);
        var promoted = controller.PromoteWorkspaceRepositoryForIndexActivity(repo2);

        Assert.IsTrue(promoted);
        Assert.AreEqual(repo2, watcher.CurrentStatus.RepositoryRoot);
        Assert.AreEqual(WorkspaceActivityReason.IndexChanged, controller.LastWorkspaceActivityReason);
        Assert.AreEqual(repo1, settings.WatchedRepositoryPath);
    }

    [TestMethod]
    public void WorkspaceModeTreatsGitIndexAsDedicatedActivityNotWorkingTreeNoise()
    {
        using var workspace = new TestRepositoryWorkspace();
        var repo1 = workspace.CreateRepo("repo1", "main");
        var repo2 = workspace.CreateRepo("repo2", "main");
        using var watcher = new GitRepositoryWatcher();
        var settings = CreateSettings(RepositoryWatchMode.WorkspaceRepo, pinnedRepository: repo1, workspaceRoot: workspace.Root);
        using var controller = CreateController(workspace, settings, watcher);

        Assert.IsTrue(controller.LoadWorkspace().Success);
        var filePromoted = controller.PromoteWorkspaceRepositoryForFileActivity(repo2, Path.Combine(repo2, ".git", "index"));
        var indexPromoted = controller.PromoteWorkspaceRepositoryForIndexActivity(repo2);

        Assert.IsFalse(filePromoted);
        Assert.IsTrue(indexPromoted);
        Assert.AreEqual(repo2, watcher.CurrentStatus.RepositoryRoot);
        Assert.AreEqual(WorkspaceActivityReason.IndexChanged, controller.LastWorkspaceActivityReason);
    }

    [TestMethod]
    public void WorkspaceModeDoesNotPromoteIgnoredFoldersAsFileActivity()
    {
        using var workspace = new TestRepositoryWorkspace();
        var repo1 = workspace.CreateRepo("repo1", "main");
        var repo2 = workspace.CreateRepo("repo2", "main");
        using var watcher = new GitRepositoryWatcher();
        var settings = CreateSettings(RepositoryWatchMode.WorkspaceRepo, pinnedRepository: repo1, workspaceRoot: workspace.Root);
        using var controller = CreateController(workspace, settings, watcher);

        Assert.IsTrue(controller.LoadWorkspace().Success);
        var promoted = controller.PromoteWorkspaceRepositoryForFileActivity(
            repo2,
            Path.Combine(repo2, "node_modules", "package", "index.js"));

        Assert.IsFalse(promoted);
        Assert.AreEqual(repo1, watcher.CurrentStatus.RepositoryRoot);
        Assert.AreEqual(WorkspaceActivityReason.WorkspaceLoaded, controller.LastWorkspaceActivityReason);
    }

    [TestMethod]
    public void WorkspaceModePromotesVscodeAndCursorFileActivityByDefault()
    {
        using var workspace = new TestRepositoryWorkspace();
        var repo1 = workspace.CreateRepo("repo1", "main");
        var repo2 = workspace.CreateRepo("repo2", "main");
        using var watcher = new GitRepositoryWatcher();
        var settings = CreateSettings(RepositoryWatchMode.WorkspaceRepo, pinnedRepository: repo1, workspaceRoot: workspace.Root);
        using var controller = CreateController(workspace, settings, watcher);

        Assert.IsTrue(controller.LoadWorkspace().Success);
        var vscodePromoted = controller.PromoteWorkspaceRepositoryForFileActivity(
            repo2,
            Path.Combine(repo2, ".vscode", "settings.json"));
        var cursorPromoted = controller.PromoteWorkspaceRepositoryForFileActivity(
            repo1,
            Path.Combine(repo1, ".cursor", "rules.md"));

        Assert.IsTrue(vscodePromoted);
        Assert.IsTrue(cursorPromoted);
        Assert.AreEqual(repo1, watcher.CurrentStatus.RepositoryRoot);
        Assert.AreEqual(WorkspaceActivityReason.FileChanged, controller.LastWorkspaceActivityReason);
    }

    [TestMethod]
    public void WorkspaceModeFileActivityCanBeDisabledWithoutDisablingBranchPromotion()
    {
        using var workspace = new TestRepositoryWorkspace();
        var repo1 = workspace.CreateRepo("repo1", "main");
        var repo2 = workspace.CreateRepo("repo2", "main");
        using var watcher = new GitRepositoryWatcher();
        var settings = CreateSettings(
            RepositoryWatchMode.WorkspaceRepo,
            pinnedRepository: repo1,
            workspaceRoot: workspace.Root,
            workspaceFileActivityEnabled: false);
        using var controller = CreateController(workspace, settings, watcher);

        Assert.IsTrue(controller.LoadWorkspace().Success);
        var filePromoted = controller.PromoteWorkspaceRepositoryForFileActivity(repo2, Path.Combine(repo2, "src", "Program.cs"));
        workspace.SetBranch(repo2, "dev");
        var branchPromoted = controller.RefreshWorkspaceRepository(repo2);

        Assert.IsFalse(filePromoted);
        Assert.IsTrue(branchPromoted);
        Assert.AreEqual(repo2, watcher.CurrentStatus.RepositoryRoot);
        Assert.AreEqual("dev", watcher.CurrentStatus.BranchDisplay);
        Assert.AreEqual(WorkspaceActivityReason.BranchChanged, controller.LastWorkspaceActivityReason);
    }

    [TestMethod]
    public void WorkspaceModeFileActivityDisabledDoesNotDisableIndexPromotion()
    {
        using var workspace = new TestRepositoryWorkspace();
        var repo1 = workspace.CreateRepo("repo1", "main");
        var repo2 = workspace.CreateRepo("repo2", "main");
        using var watcher = new GitRepositoryWatcher();
        var settings = CreateSettings(
            RepositoryWatchMode.WorkspaceRepo,
            pinnedRepository: repo1,
            workspaceRoot: workspace.Root,
            workspaceFileActivityEnabled: false);
        using var controller = CreateController(workspace, settings, watcher);

        Assert.IsTrue(controller.LoadWorkspace().Success);
        var promoted = controller.PromoteWorkspaceRepositoryForIndexActivity(repo2);

        Assert.IsTrue(promoted);
        Assert.AreEqual(repo2, watcher.CurrentStatus.RepositoryRoot);
        Assert.AreEqual(WorkspaceActivityReason.IndexChanged, controller.LastWorkspaceActivityReason);
    }

    [TestMethod]
    public void WorkspaceModeAvoidsRepeatedUiChurnForSameActiveRepoFileActivity()
    {
        using var workspace = new TestRepositoryWorkspace();
        var repo = workspace.CreateRepo("repo", "main");
        using var watcher = new GitRepositoryWatcher();
        var settings = CreateSettings(RepositoryWatchMode.WorkspaceRepo, pinnedRepository: repo, workspaceRoot: workspace.Root);
        using var controller = CreateController(workspace, settings, watcher);
        var stateChanges = 0;
        controller.StateChanged += (_, _) => stateChanges++;

        Assert.IsTrue(controller.LoadWorkspace().Success);
        var afterLoad = stateChanges;
        Assert.IsTrue(controller.PromoteWorkspaceRepositoryForFileActivity(repo, Path.Combine(repo, "src", "Program.cs")));
        var afterFirstFileActivity = stateChanges;
        Assert.IsTrue(controller.PromoteWorkspaceRepositoryForFileActivity(repo, Path.Combine(repo, "src", "Other.cs")));

        Assert.AreEqual(afterLoad + 1, afterFirstFileActivity);
        Assert.AreEqual(afterFirstFileActivity, stateChanges);
        Assert.AreEqual(repo, watcher.CurrentStatus.RepositoryRoot);
    }

    [TestMethod]
    public void WorkspaceModeDoesNotPromoteOnSameBranchRefUpdate()
    {
        using var workspace = new TestRepositoryWorkspace();
        var repo1 = workspace.CreateRepo("repo1", "main");
        var repo2 = workspace.CreateRepo("repo2", "main");
        using var watcher = new GitRepositoryWatcher();
        var settings = CreateSettings(RepositoryWatchMode.WorkspaceRepo, pinnedRepository: repo1, workspaceRoot: workspace.Root);
        using var controller = CreateController(workspace, settings, watcher);

        Assert.IsTrue(controller.LoadWorkspace().Success);
        workspace.UpdateBranchRef(repo2, "main");
        var promoted = controller.RefreshWorkspaceRepository(repo2);

        Assert.IsFalse(promoted);
        Assert.AreEqual(repo1, watcher.CurrentStatus.RepositoryRoot);
    }

    [TestMethod]
    public void SelectingWorkspaceDoesNotOverwritePinnedRepository()
    {
        using var workspace = new TestRepositoryWorkspace();
        var pinnedRepo = workspace.CreateRepo("pinned");
        workspace.CreateRepo("workspace-repo");
        using var watcher = new GitRepositoryWatcher();
        var settings = CreateSettings(RepositoryWatchMode.PinnedRepo, pinnedRepository: pinnedRepo);
        using var controller = CreateController(workspace, settings, watcher);

        var result = controller.SelectWorkspace(workspace.Root);

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.AreEqual(pinnedRepo, settings.WatchedRepositoryPath);
        Assert.AreEqual(workspace.Root, settings.WorkspaceRootPath);
        Assert.AreEqual(RepositoryWatchMode.WorkspaceRepo, settings.WatchMode);
    }

    [TestMethod]
    public void WorkspaceModeFileActivityDoesNotOverwritePinnedRepository()
    {
        using var workspace = new TestRepositoryWorkspace();
        var pinnedRepo = workspace.CreateRepo("pinned");
        var activeRepo = workspace.CreateRepo("active");
        using var watcher = new GitRepositoryWatcher();
        var settings = CreateSettings(RepositoryWatchMode.WorkspaceRepo, pinnedRepository: pinnedRepo, workspaceRoot: workspace.Root);
        using var controller = CreateController(workspace, settings, watcher);

        Assert.IsTrue(controller.LoadWorkspace().Success);
        var promoted = controller.PromoteWorkspaceRepositoryForFileActivity(
            activeRepo,
            Path.Combine(activeRepo, "src", "Program.cs"));

        Assert.IsTrue(promoted);
        Assert.AreEqual(activeRepo, watcher.CurrentStatus.RepositoryRoot);
        Assert.AreEqual(pinnedRepo, settings.WatchedRepositoryPath);
    }

    [TestMethod]
    public void PinnedRepoModeIgnoresWorkspaceFileActivity()
    {
        using var workspace = new TestRepositoryWorkspace();
        var repo1 = workspace.CreateRepo("repo1");
        var repo2 = workspace.CreateRepo("repo2");
        using var watcher = new GitRepositoryWatcher();
        var settings = CreateSettings(RepositoryWatchMode.WorkspaceRepo, pinnedRepository: repo1, workspaceRoot: workspace.Root);
        using var controller = CreateController(workspace, settings, watcher);

        Assert.IsTrue(controller.LoadWorkspace().Success);
        controller.SetWatchMode(RepositoryWatchMode.PinnedRepo);
        var promoted = controller.PromoteWorkspaceRepositoryForFileActivity(repo2, Path.Combine(repo2, "src", "Program.cs"));

        Assert.IsFalse(promoted);
        Assert.AreEqual(repo1, watcher.CurrentStatus.RepositoryRoot);
        Assert.IsNull(controller.LastWorkspaceActivityReason);
    }

    [TestMethod]
    public void WorkspaceModePromotesRepositoryOnRepoOpened()
    {
        using var workspace = new TestRepositoryWorkspace();
        var repo1 = workspace.CreateRepo("repo1", "main");
        var repo2 = workspace.CreateRepo("repo2", "main");
        using var watcher = new GitRepositoryWatcher();
        var settings = CreateSettings(RepositoryWatchMode.WorkspaceRepo, pinnedRepository: repo1, workspaceRoot: workspace.Root);
        using var controller = CreateController(workspace, settings, watcher);

        Assert.IsTrue(controller.LoadWorkspace().Success);
        var promoted = controller.PromoteWorkspaceRepositoryForRepoOpened(repo2);

        Assert.IsTrue(promoted);
        Assert.AreEqual(repo2, watcher.CurrentStatus.RepositoryRoot);
        Assert.AreEqual(WorkspaceActivityReason.RepoOpened, controller.LastWorkspaceActivityReason);
        Assert.AreEqual(repo1, settings.WatchedRepositoryPath);
    }

    [TestMethod]
    public void WorkspaceModeRepoOpenedDoesNotOverwritePinnedRepository()
    {
        using var workspace = new TestRepositoryWorkspace();
        var pinnedRepo = workspace.CreateRepo("pinned");
        var activeRepo = workspace.CreateRepo("active");
        using var watcher = new GitRepositoryWatcher();
        var settings = CreateSettings(RepositoryWatchMode.WorkspaceRepo, pinnedRepository: pinnedRepo, workspaceRoot: workspace.Root);
        using var controller = CreateController(workspace, settings, watcher);

        Assert.IsTrue(controller.LoadWorkspace().Success);
        var promoted = controller.PromoteWorkspaceRepositoryForRepoOpened(activeRepo);

        Assert.IsTrue(promoted);
        Assert.AreEqual(activeRepo, watcher.CurrentStatus.RepositoryRoot);
        Assert.AreEqual(pinnedRepo, settings.WatchedRepositoryPath);
    }

    [TestMethod]
    public void WorkspaceModeRepoOpenedOutsideDiscoveredWorkspaceIsIgnored()
    {
        using var workspace = new TestRepositoryWorkspace();
        workspace.CreateRepo("repo1");
        var outsideRepo = Path.Combine(Path.GetTempPath(), "BranchWatchOutside", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(outsideRepo, ".git", "refs", "heads"));
        File.WriteAllText(Path.Combine(outsideRepo, ".git", "HEAD"), "ref: refs/heads/main");
        File.WriteAllText(Path.Combine(outsideRepo, ".git", "refs", "heads", "main"), "0123456789abcdef0123456789abcdef01234567");
        using var watcher = new GitRepositoryWatcher();
        var settings = CreateSettings(RepositoryWatchMode.WorkspaceRepo, workspaceRoot: workspace.Root);
        using var controller = CreateController(workspace, settings, watcher);

        try
        {
            Assert.IsTrue(controller.LoadWorkspace().Success);
            var result = controller.TryReportRepoOpened(outsideRepo);

            Assert.IsTrue(result.Success);
            Assert.IsFalse(result.Promoted);
            StringAssert.Contains(result.Message, "Rescan workspace");
            StringAssert.Contains(result.Message, "WorkspaceDiscoveryMaxDepth");
            Assert.AreEqual(WorkspaceActivityReason.WorkspaceLoaded, controller.LastWorkspaceActivityReason);
        }
        finally
        {
            Directory.Delete(outsideRepo, recursive: true);
        }
    }

    [TestMethod]
    public void WorkspaceModeRepoOpenedBeyondDiscoveryDepthIsIgnored()
    {
        using var workspace = new TestRepositoryWorkspace();
        workspace.CreateRepo("repo1");
        var deepRepo = workspace.CreateNestedDirectory(workspace.Root, "level1", "level2", "level3", "deep-repo");
        Directory.CreateDirectory(Path.Combine(deepRepo, ".git", "refs", "heads"));
        File.WriteAllText(Path.Combine(deepRepo, ".git", "HEAD"), "ref: refs/heads/main");
        File.WriteAllText(Path.Combine(deepRepo, ".git", "refs", "heads", "main"), "0123456789abcdef0123456789abcdef01234567");
        using var watcher = new GitRepositoryWatcher();
        var settings = CreateSettings(
            RepositoryWatchMode.WorkspaceRepo,
            workspaceRoot: workspace.Root,
            workspaceDiscoveryMaxDepth: 2);
        using var controller = CreateController(workspace, settings, watcher);

        Assert.IsTrue(controller.LoadWorkspace().Success);
        var result = controller.TryReportRepoOpened(deepRepo);

        Assert.IsTrue(result.Success);
        Assert.IsFalse(result.Promoted);
        StringAssert.Contains(result.Message, "Rescan workspace");
        StringAssert.Contains(result.Message, "WorkspaceDiscoveryMaxDepth");
    }

    [TestMethod]
    public void PinnedRepoModeIgnoresRepoOpenedActivity()
    {
        using var workspace = new TestRepositoryWorkspace();
        var repo1 = workspace.CreateRepo("repo1");
        var repo2 = workspace.CreateRepo("repo2");
        using var watcher = new GitRepositoryWatcher();
        var settings = CreateSettings(RepositoryWatchMode.WorkspaceRepo, pinnedRepository: repo1, workspaceRoot: workspace.Root);
        using var controller = CreateController(workspace, settings, watcher);

        Assert.IsTrue(controller.LoadWorkspace().Success);
        controller.SetWatchMode(RepositoryWatchMode.PinnedRepo);
        var result = controller.TryReportRepoOpened(repo2);

        Assert.IsTrue(result.Success);
        Assert.IsFalse(result.Promoted);
        StringAssert.Contains(result.Message, "WorkspaceRepo mode");
        Assert.AreEqual(repo1, watcher.CurrentStatus.RepositoryRoot);
        Assert.IsNull(controller.LastWorkspaceActivityReason);
    }

    private static AppSettings CreateSettings(
        RepositoryWatchMode watchMode,
        string? pinnedRepository = null,
        string? workspaceRoot = null,
        bool workspaceFileActivityEnabled = true,
        int workspaceDiscoveryMaxDepth = 2)
    {
        return new AppSettings
        {
            WatchMode = watchMode,
            WatchedRepositoryPath = pinnedRepository,
            WorkspaceRootPath = workspaceRoot,
            WorkspaceFileActivityEnabled = workspaceFileActivityEnabled,
            WorkspaceDiscoveryMaxDepth = workspaceDiscoveryMaxDepth
        };
    }

    private static RepositorySessionController CreateController(
        TestRepositoryWorkspace workspace,
        AppSettings settings,
        GitRepositoryWatcher watcher)
    {
        var settingsPath = Path.Combine(workspace.Root, "settings.json");
        return new RepositorySessionController(new SettingsService(settingsPath), settings, watcher);
    }
}

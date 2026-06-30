namespace BranchWatch.Tests;

[TestClass]
public sealed class WorkspaceRepositoryScannerTests
{
    [TestMethod]
    public void DiscoversMultipleRepositoriesUnderWorkspace()
    {
        using var workspace = new TestRepositoryWorkspace();
        var repo1 = workspace.CreateRepo("repo1");
        var repo2 = workspace.CreateRepo("nested/repo2");
        var scanner = new WorkspaceRepositoryScanner();

        var result = scanner.Scan(workspace.Root);

        Assert.IsTrue(result.Success, result.ErrorMessage);
        CollectionAssert.AreEquivalent(
            new[] { repo1, repo2 },
            result.Repositories.Select(repository => repository.RootPath).ToArray());
    }

    [TestMethod]
    public void DoesNotDiscoverGitMetadataInternalsAsRepositories()
    {
        using var workspace = new TestRepositoryWorkspace();
        var repo = workspace.CreateRepo("repo");
        var fakeInternalRepo = Path.Combine(repo, ".git", "objects", "nested");
        Directory.CreateDirectory(Path.Combine(fakeInternalRepo, ".git"));
        var scanner = new WorkspaceRepositoryScanner();

        var result = scanner.Scan(workspace.Root);

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.HasCount(1, result.Repositories);
        Assert.AreEqual(repo, result.Repositories[0].RootPath);
    }

    [TestMethod]
    public void SupportsGitFileWorktreeRepositories()
    {
        using var workspace = new TestRepositoryWorkspace();
        var repo = workspace.CreateGitFileRepo("worktree");
        var scanner = new WorkspaceRepositoryScanner();

        var result = scanner.Scan(workspace.Root);

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.AreEqual(repo, result.Repositories.Single().RootPath);
    }

    [TestMethod]
    public void DepthZeroDiscoversOnlyWorkspaceRootRepository()
    {
        using var workspace = new TestRepositoryWorkspace();
        var rootRepo = workspace.CreateRootRepo();
        workspace.CreateRepo("child");
        var scanner = new WorkspaceRepositoryScanner();

        var result = scanner.Scan(workspace.Root, maxDepth: 0);

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.HasCount(1, result.Repositories);
        Assert.AreEqual(rootRepo, result.Repositories[0].RootPath);
    }

    [TestMethod]
    public void DefaultDepthDiscoversDirectChildRepositories()
    {
        using var workspace = new TestRepositoryWorkspace();
        var repo = workspace.CreateRepo("child");
        var scanner = new WorkspaceRepositoryScanner();

        var result = scanner.Scan(workspace.Root);

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.HasCount(1, result.Repositories);
        Assert.AreEqual(repo, result.Repositories[0].RootPath);
    }

    [TestMethod]
    public void DoesNotDiscoverRepositoriesDeeperThanMaxDepth()
    {
        using var workspace = new TestRepositoryWorkspace();
        var repo = workspace.CreateRepo("one/two/repo");
        var scanner = new WorkspaceRepositoryScanner();

        var shallowResult = scanner.Scan(workspace.Root, maxDepth: 2);
        var deepResult = scanner.Scan(workspace.Root, maxDepth: 3);

        Assert.IsTrue(shallowResult.Success, shallowResult.ErrorMessage);
        Assert.IsEmpty(shallowResult.Repositories);
        Assert.IsTrue(deepResult.Success, deepResult.ErrorMessage);
        Assert.AreEqual(repo, deepResult.Repositories.Single().RootPath);
    }

    [TestMethod]
    public void DoesNotDescendIntoNodeModules()
    {
        using var workspace = new TestRepositoryWorkspace();
        var repo = workspace.CreateRepo("repo");
        workspace.CreateRepo("node_modules/ignored");
        var scanner = new WorkspaceRepositoryScanner();

        var result = scanner.Scan(workspace.Root);

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.HasCount(1, result.Repositories);
        Assert.AreEqual(repo, result.Repositories[0].RootPath);
    }

    [TestMethod]
    public void DoesNotDescendIntoGeneratedFolders()
    {
        using var workspace = new TestRepositoryWorkspace();
        var repo = workspace.CreateRepo("repo");
        foreach (var ignoredFolder in new[] { "bin", "obj", "dist", "build", "target" })
        {
            workspace.CreateRepo($"{ignoredFolder}/ignored");
        }

        var scanner = new WorkspaceRepositoryScanner();

        var result = scanner.Scan(workspace.Root);

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.HasCount(1, result.Repositories);
        Assert.AreEqual(repo, result.Repositories[0].RootPath);
    }

    [TestMethod]
    public void HandlesWorkspaceWithNoRepositories()
    {
        using var workspace = new TestRepositoryWorkspace();
        workspace.CreatePlainDirectory("plain");
        var scanner = new WorkspaceRepositoryScanner();

        var result = scanner.Scan(workspace.Root);

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.IsEmpty(result.Repositories);
    }
}

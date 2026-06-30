namespace BranchWatch.Tests;

[TestClass]
public sealed class GitRepositoryResolverTests
{
    [TestMethod]
    public void ResolvesGitDirectoryRepoFromNestedDirectory()
    {
        using var workspace = new TestRepositoryWorkspace();
        var repo = workspace.CreateRepo("repo");
        var nested = workspace.CreateNestedDirectory(repo, "src", "feature");

        var resolved = GitRepositoryResolver.TryResolveRepository(nested, out var repository, out var error);

        Assert.IsTrue(resolved, error);
        Assert.AreEqual(repo, repository.RootPath);
        Assert.AreEqual(Path.Combine(repo, ".git"), repository.GitDirectory);
    }

    [TestMethod]
    public void ResolvesGitFileRepoFromNestedDirectory()
    {
        using var workspace = new TestRepositoryWorkspace();
        var repo = workspace.CreateGitFileRepo("worktree");
        var nested = workspace.CreateNestedDirectory(repo, "src", "feature");

        var resolved = GitRepositoryResolver.TryResolveRepository(nested, out var repository, out var error);

        Assert.IsTrue(resolved, error);
        Assert.AreEqual(repo, repository.RootPath);
        Assert.AreEqual(
            TestRepositoryWorkspace.Normalize(Path.Combine(workspace.Root, "metadata", "worktree.git")),
            repository.GitDirectory);
    }

    [TestMethod]
    public void ReturnsFalseWhenNoRepositoryIsFound()
    {
        using var workspace = new TestRepositoryWorkspace();
        var plainDirectory = workspace.CreatePlainDirectory("plain");
        var nested = workspace.CreateNestedDirectory(plainDirectory, "src");

        var resolved = GitRepositoryResolver.TryResolveRepository(nested, out _, out var error);

        Assert.IsFalse(resolved);
        Assert.AreEqual("The selected folder is not inside a Git repository.", error);
    }
}

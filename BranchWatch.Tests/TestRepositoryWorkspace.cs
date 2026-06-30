namespace BranchWatch.Tests;

internal sealed class TestRepositoryWorkspace : IDisposable
{
    public string Root { get; } = Path.Combine(
        Path.GetTempPath(),
        "BranchWatchTests",
        Guid.NewGuid().ToString("N"));

    public TestRepositoryWorkspace()
    {
        Directory.CreateDirectory(Root);
    }

    public string CreateRepo(string name, string branch = "main")
    {
        var repo = Path.Combine(Root, name);
        CreateGitMetadata(repo, branch);
        return Normalize(repo);
    }

    public string CreateRootRepo(string branch = "main")
    {
        CreateGitMetadata(Root, branch);
        return Normalize(Root);
    }

    private static void CreateGitMetadata(string repo, string branch)
    {
        var refsDirectory = Path.Combine(repo, ".git", "refs", "heads");
        Directory.CreateDirectory(refsDirectory);
        File.WriteAllText(Path.Combine(repo, ".git", "HEAD"), $"ref: refs/heads/{branch}");
        File.WriteAllText(Path.Combine(refsDirectory, branch), "0123456789abcdef0123456789abcdef01234567");
    }

    public string CreateGitFileRepo(string name, string branch = "main")
    {
        var repo = Path.Combine(Root, name);
        var gitDirectory = Path.Combine(Root, "metadata", $"{name}.git");
        var refsDirectory = Path.Combine(gitDirectory, "refs", "heads");

        Directory.CreateDirectory(repo);
        Directory.CreateDirectory(refsDirectory);
        File.WriteAllText(Path.Combine(repo, ".git"), $@"gitdir: ..\metadata\{name}.git");
        File.WriteAllText(Path.Combine(gitDirectory, "HEAD"), $"ref: refs/heads/{branch}");
        File.WriteAllText(Path.Combine(refsDirectory, branch), "0123456789abcdef0123456789abcdef01234567");
        return Normalize(repo);
    }

    public string CreateNestedDirectory(string root, params string[] parts)
    {
        var path = Path.Combine([root, .. parts]);
        Directory.CreateDirectory(path);
        return Normalize(path);
    }

    public string CreatePlainDirectory(string name)
    {
        var path = Path.Combine(Root, name);
        Directory.CreateDirectory(path);
        return Normalize(path);
    }

    public void SetBranch(string repo, string branch)
    {
        var refsDirectory = Path.Combine(repo, ".git", "refs", "heads");
        Directory.CreateDirectory(refsDirectory);
        File.WriteAllText(Path.Combine(repo, ".git", "HEAD"), $"ref: refs/heads/{branch}");
        File.WriteAllText(Path.Combine(refsDirectory, branch), "abcdef0123456789abcdef0123456789abcdef01");
    }

    public void UpdateBranchRef(string repo, string branch)
    {
        var refsDirectory = Path.Combine(repo, ".git", "refs", "heads");
        Directory.CreateDirectory(refsDirectory);
        File.WriteAllText(Path.Combine(refsDirectory, branch), Guid.NewGuid().ToString("N").PadRight(40, '0')[..40]);
    }

    public static string Normalize(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch
        {
        }
    }
}

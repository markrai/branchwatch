using System.IO;

namespace BranchWatch;

internal sealed class WorkspaceRepositoryScanner
{
    public WorkspaceScanResult Scan(string workspaceRootPath, int maxDepth = 2)
    {
        if (string.IsNullOrWhiteSpace(workspaceRootPath))
        {
            return WorkspaceScanResult.Failed("Choose a workspace folder.");
        }

        string root;
        try
        {
            root = NormalizeDirectoryPath(Path.GetFullPath(Environment.ExpandEnvironmentVariables(workspaceRootPath.Trim())));
        }
        catch (Exception ex)
        {
            return WorkspaceScanResult.Failed($"The workspace path is not valid: {ex.Message}");
        }

        if (!Directory.Exists(root))
        {
            return WorkspaceScanResult.Failed($"The workspace folder does not exist:\n{root}");
        }

        var repositories = new Dictionary<string, RepositoryInfo>(StringComparer.OrdinalIgnoreCase);
        var boundedMaxDepth = Math.Max(0, maxDepth);
        var pending = new Stack<PendingDirectory>();
        pending.Push(new PendingDirectory(root, 0));

        while (pending.Count > 0)
        {
            var pendingDirectory = pending.Pop();
            var directory = pendingDirectory.Path;
            if (HasGitMetadata(directory)
                && GitRepositoryResolver.TryResolveRepository(directory, out var repository, out _))
            {
                repositories.TryAdd(repository.RootPath, repository);
            }

            if (pendingDirectory.Depth >= boundedMaxDepth)
            {
                continue;
            }

            foreach (var child in EnumerateChildDirectories(directory))
            {
                if (WorkspaceIgnorePolicy.ShouldIgnoreDirectory(child))
                {
                    continue;
                }

                pending.Push(new PendingDirectory(child, pendingDirectory.Depth + 1));
            }
        }

        return WorkspaceScanResult.Succeeded(
            root,
            repositories.Values.OrderBy(repository => repository.RootPath, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static bool HasGitMetadata(string directory)
    {
        var gitPath = Path.Combine(directory, ".git");
        return Directory.Exists(gitPath) || File.Exists(gitPath);
    }

    private static IEnumerable<string> EnumerateChildDirectories(string directory)
    {
        try
        {
            return Directory.EnumerateDirectories(directory).ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string NormalizeDirectoryPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private sealed record PendingDirectory(string Path, int Depth);
}

internal sealed record WorkspaceScanResult(
    bool Success,
    string? WorkspaceRootPath,
    IReadOnlyList<RepositoryInfo> Repositories,
    string? ErrorMessage)
{
    public static WorkspaceScanResult Succeeded(string workspaceRootPath, IReadOnlyList<RepositoryInfo> repositories) =>
        new(true, workspaceRootPath, repositories, null);

    public static WorkspaceScanResult Failed(string errorMessage) =>
        new(false, null, Array.Empty<RepositoryInfo>(), errorMessage);
}

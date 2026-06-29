using System.IO;

namespace BranchWatch;

internal static class GitBranchReader
{
    public static RepositoryStatus ReadStatus(RepositoryInfo repository)
    {
        var headPath = Path.Combine(repository.GitDirectory, "HEAD");
        if (!File.Exists(headPath))
        {
            return RepositoryStatus.FromError(repository, "Git HEAD was not found.");
        }

        try
        {
            var head = File.ReadAllText(headPath).Trim();
            if (string.IsNullOrWhiteSpace(head))
            {
                return RepositoryStatus.FromError(repository, "Git HEAD is empty.");
            }

            if (head.StartsWith("ref:", StringComparison.OrdinalIgnoreCase))
            {
                var refName = head["ref:".Length..].Trim();
                var branchName = GetBranchDisplayName(refName);
                var refPath = FindExistingRefPath(repository, refName);

                return new RepositoryStatus(
                    repository.RootPath,
                    repository.GitDirectory,
                    repository.CommonDirectory,
                    branchName,
                    refName,
                    refPath,
                    null,
                    null);
            }

            return new RepositoryStatus(
                repository.RootPath,
                repository.GitDirectory,
                repository.CommonDirectory,
                $"DETACHED @ {ShortSha(head)}",
                null,
                null,
                head,
                null);
        }
        catch (Exception ex)
        {
            return RepositoryStatus.FromError(repository, $"Unable to read Git branch metadata: {ex.Message}");
        }
    }

    private static string GetBranchDisplayName(string refName)
    {
        const string headsPrefix = "refs/heads/";
        return refName.StartsWith(headsPrefix, StringComparison.Ordinal)
            ? refName[headsPrefix.Length..]
            : refName;
    }

    private static string ShortSha(string value)
    {
        var sha = value.Trim();
        return sha.Length <= 8 ? sha : sha[..8];
    }

    private static string? FindExistingRefPath(RepositoryInfo repository, string refName)
    {
        var localRefPath = Path.Combine(repository.GitDirectory, PathFromGitRef(refName));
        if (File.Exists(localRefPath))
        {
            return localRefPath;
        }

        var commonRefPath = Path.Combine(repository.CommonDirectory, PathFromGitRef(refName));
        return File.Exists(commonRefPath) ? commonRefPath : null;
    }

    private static string PathFromGitRef(string refName)
    {
        return refName.Replace('/', Path.DirectorySeparatorChar);
    }
}

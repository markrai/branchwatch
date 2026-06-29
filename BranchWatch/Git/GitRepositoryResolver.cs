using System.IO;

namespace BranchWatch;

internal static class GitRepositoryResolver
{
    public static bool TryResolveRepository(string selectedPath, out RepositoryInfo repository, out string error)
    {
        repository = default!;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            error = "Choose a folder inside a Git repository.";
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(selectedPath.Trim()));
        }
        catch (Exception ex)
        {
            error = $"The selected path is not valid: {ex.Message}";
            return false;
        }

        if (File.Exists(fullPath))
        {
            fullPath = Path.GetDirectoryName(fullPath) ?? fullPath;
        }

        if (!Directory.Exists(fullPath))
        {
            error = $"The selected folder does not exist:\n{fullPath}";
            return false;
        }

        var directory = new DirectoryInfo(fullPath);
        while (directory is not null)
        {
            var gitPath = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(gitPath))
            {
                var gitDirectory = NormalizeDirectoryPath(gitPath);
                repository = new RepositoryInfo(
                    NormalizeDirectoryPath(directory.FullName),
                    gitDirectory,
                    ResolveCommonDirectory(gitDirectory));
                return true;
            }

            if (File.Exists(gitPath))
            {
                if (!TryReadGitDirectoryFile(gitPath, directory.FullName, out var gitDirectory, out error))
                {
                    return false;
                }

                repository = new RepositoryInfo(
                    NormalizeDirectoryPath(directory.FullName),
                    gitDirectory,
                    ResolveCommonDirectory(gitDirectory));
                return true;
            }

            directory = directory.Parent;
        }

        error = "The selected folder is not inside a Git repository.";
        return false;
    }

    private static bool TryReadGitDirectoryFile(string gitFilePath, string repositoryRoot, out string gitDirectory, out string error)
    {
        gitDirectory = string.Empty;
        error = string.Empty;

        string line;
        try
        {
            line = File.ReadLines(gitFilePath).FirstOrDefault()?.Trim() ?? string.Empty;
        }
        catch (Exception ex)
        {
            error = $"Unable to read Git metadata:\n{ex.Message}";
            return false;
        }

        if (!line.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase))
        {
            error = $"The .git file is not a supported Git metadata pointer:\n{gitFilePath}";
            return false;
        }

        var rawPath = line["gitdir:".Length..].Trim();
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            error = $"The .git file does not contain a Git metadata path:\n{gitFilePath}";
            return false;
        }

        var resolvedPath = Path.IsPathRooted(rawPath)
            ? rawPath
            : Path.GetFullPath(Path.Combine(repositoryRoot, rawPath));

        if (!Directory.Exists(resolvedPath))
        {
            error = $"The Git metadata directory does not exist:\n{resolvedPath}";
            return false;
        }

        gitDirectory = NormalizeDirectoryPath(resolvedPath);
        return true;
    }

    private static string ResolveCommonDirectory(string gitDirectory)
    {
        var commonDirPath = Path.Combine(gitDirectory, "commondir");
        if (!File.Exists(commonDirPath))
        {
            return gitDirectory;
        }

        try
        {
            var rawPath = File.ReadLines(commonDirPath).FirstOrDefault()?.Trim();
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                return gitDirectory;
            }

            var resolvedPath = Path.IsPathRooted(rawPath)
                ? rawPath
                : Path.GetFullPath(Path.Combine(gitDirectory, rawPath));

            return Directory.Exists(resolvedPath) ? NormalizeDirectoryPath(resolvedPath) : gitDirectory;
        }
        catch
        {
            return gitDirectory;
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
}

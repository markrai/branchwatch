using System.IO;

namespace BranchWatch;

internal static class WorkspaceIgnorePolicy
{
    private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        "node_modules",
        "bin",
        "obj",
        "dist",
        "build",
        "target",
        "tmp",
        "temp",
        "vendor",
        "packages",
        ".next",
        ".nuxt",
        ".cache",
        "coverage"
    };

    public static bool ShouldIgnoreDirectory(string directoryPath)
    {
        var directoryName = Path.GetFileName(directoryPath);
        return !string.IsNullOrWhiteSpace(directoryName) && IgnoredDirectoryNames.Contains(directoryName);
    }

    public static bool IsIgnoredRelativePath(string relativePath)
    {
        var segments = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        return segments.Any(segment => IgnoredDirectoryNames.Contains(segment));
    }
}

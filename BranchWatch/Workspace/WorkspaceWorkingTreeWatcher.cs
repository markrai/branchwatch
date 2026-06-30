using System.IO;

namespace BranchWatch;

internal sealed class WorkspaceWorkingTreeWatcher : IDisposable
{
    private readonly string _repositoryRoot;
    private readonly Action _scheduleActivity;
    private readonly Action _scheduleRecovery;
    private FileSystemWatcher? _watcher;

    public WorkspaceWorkingTreeWatcher(
        string repositoryRoot,
        Action scheduleActivity,
        Action scheduleRecovery)
    {
        _repositoryRoot = repositoryRoot;
        _scheduleActivity = scheduleActivity;
        _scheduleRecovery = scheduleRecovery;
        _watcher = TryCreateWatcher(repositoryRoot);
    }

    public void Dispose()
    {
        if (_watcher is null)
        {
            return;
        }

        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _watcher = null;
    }

    internal static bool IsWorkingTreeActivityPath(string repositoryRoot, string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var relativePath = Path.GetRelativePath(repositoryRoot, path);
            if (string.IsNullOrWhiteSpace(relativePath)
                || relativePath == "."
                || relativePath == ".."
                || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
            {
                return false;
            }

            var segments = relativePath.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);

            return segments.Length > 0 && !WorkspaceIgnorePolicy.IsIgnoredRelativePath(relativePath);
        }
        catch
        {
            return false;
        }
    }

    private FileSystemWatcher? TryCreateWatcher(string repositoryRoot)
    {
        try
        {
            if (!Directory.Exists(repositoryRoot))
            {
                return null;
            }

            var watcher = new FileSystemWatcher(repositoryRoot)
            {
                NotifyFilter = NotifyFilters.FileName
                    | NotifyFilters.DirectoryName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.Size
                    | NotifyFilters.CreationTime,
                IncludeSubdirectories = true,
                InternalBufferSize = 64 * 1024,
                EnableRaisingEvents = true
            };

            watcher.Changed += OnWatchedPathChanged;
            watcher.Created += OnWatchedPathChanged;
            watcher.Deleted += OnWatchedPathChanged;
            watcher.Renamed += OnWatchedPathRenamed;
            watcher.Error += OnWatcherError;
            return watcher;
        }
        catch
        {
            return null;
        }
    }

    private void OnWatchedPathChanged(object sender, FileSystemEventArgs e)
    {
        if (IsWorkingTreeActivityPath(_repositoryRoot, e.FullPath))
        {
            _scheduleActivity();
        }
    }

    private void OnWatchedPathRenamed(object sender, RenamedEventArgs e)
    {
        if (IsWorkingTreeActivityPath(_repositoryRoot, e.FullPath)
            || IsWorkingTreeActivityPath(_repositoryRoot, e.OldFullPath))
        {
            _scheduleActivity();
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _scheduleRecovery();
    }
}

using System.IO;

namespace BranchWatch;

internal sealed class GitMetadataWatchers : IDisposable
{
    private readonly Action _scheduleRefresh;
    private FileSystemWatcher? _headWatcher;
    private FileSystemWatcher? _refWatcher;
    private FileSystemWatcher? _packedRefsWatcher;

    public GitMetadataWatchers(Action scheduleRefresh)
    {
        _scheduleRefresh = scheduleRefresh;
    }

    public void Rebuild(RepositoryInfo repository, RepositoryStatus status)
    {
        DisposeWatchers();

        _headWatcher = TryCreateFileWatcher(repository.GitDirectory, "HEAD");

        if (!string.IsNullOrWhiteSpace(status.RefPath))
        {
            var refDirectory = Path.GetDirectoryName(status.RefPath);
            var refFile = Path.GetFileName(status.RefPath);
            if (!string.IsNullOrWhiteSpace(refDirectory) && !string.IsNullOrWhiteSpace(refFile))
            {
                _refWatcher = TryCreateFileWatcher(refDirectory, refFile);
            }
        }

        var packedRefsPath = Path.Combine(repository.CommonDirectory, "packed-refs");
        if (File.Exists(packedRefsPath))
        {
            _packedRefsWatcher = TryCreateFileWatcher(repository.CommonDirectory, "packed-refs");
        }
    }

    public void Dispose()
    {
        DisposeWatchers();
    }

    private FileSystemWatcher? TryCreateFileWatcher(string directory, string filter)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return null;
            }

            var watcher = new FileSystemWatcher(directory, filter)
            {
                NotifyFilter = NotifyFilters.FileName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.Size
                    | NotifyFilters.CreationTime,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };

            watcher.Changed += OnWatchedFileChanged;
            watcher.Created += OnWatchedFileChanged;
            watcher.Deleted += OnWatchedFileChanged;
            watcher.Renamed += OnWatchedFileRenamed;
            return watcher;
        }
        catch
        {
            return null;
        }
    }

    private void DisposeWatchers()
    {
        DisposeWatcher(_headWatcher);
        DisposeWatcher(_refWatcher);
        DisposeWatcher(_packedRefsWatcher);
        _headWatcher = null;
        _refWatcher = null;
        _packedRefsWatcher = null;
    }

    private static void DisposeWatcher(FileSystemWatcher? watcher)
    {
        if (watcher is null)
        {
            return;
        }

        watcher.EnableRaisingEvents = false;
        watcher.Dispose();
    }

    private void OnWatchedFileChanged(object sender, FileSystemEventArgs e)
    {
        _scheduleRefresh();
    }

    private void OnWatchedFileRenamed(object sender, RenamedEventArgs e)
    {
        _scheduleRefresh();
    }
}

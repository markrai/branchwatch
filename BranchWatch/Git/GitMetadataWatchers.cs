using System.IO;

namespace BranchWatch;

internal sealed class GitMetadataWatchers : IDisposable
{
    private readonly Action _scheduleRefresh;
    private readonly Action? _scheduleIndexActivity;
    private FileSystemWatcher? _headWatcher;
    private FileSystemWatcher? _refWatcher;
    private FileSystemWatcher? _packedRefsWatcher;
    private FileSystemWatcher? _indexWatcher;

    public GitMetadataWatchers(Action scheduleRefresh, Action? scheduleIndexActivity = null)
    {
        _scheduleRefresh = scheduleRefresh;
        _scheduleIndexActivity = scheduleIndexActivity;
    }

    public void Rebuild(RepositoryInfo repository, RepositoryStatus status)
    {
        DisposeWatchers();

        _headWatcher = TryCreateFileWatcher(repository.GitDirectory, "HEAD", _scheduleRefresh);

        if (!string.IsNullOrWhiteSpace(status.RefPath))
        {
            var refDirectory = Path.GetDirectoryName(status.RefPath);
            var refFile = Path.GetFileName(status.RefPath);
            if (!string.IsNullOrWhiteSpace(refDirectory) && !string.IsNullOrWhiteSpace(refFile))
            {
                _refWatcher = TryCreateFileWatcher(refDirectory, refFile, _scheduleRefresh);
            }
        }

        var packedRefsPath = Path.Combine(repository.CommonDirectory, "packed-refs");
        if (File.Exists(packedRefsPath))
        {
            _packedRefsWatcher = TryCreateFileWatcher(repository.CommonDirectory, "packed-refs", _scheduleRefresh);
        }

        if (_scheduleIndexActivity is not null)
        {
            _indexWatcher = TryCreateFileWatcher(repository.GitDirectory, "index", _scheduleIndexActivity);
        }
    }

    public void Clear()
    {
        DisposeWatchers();
    }

    public void Dispose()
    {
        DisposeWatchers();
    }

    private FileSystemWatcher? TryCreateFileWatcher(string directory, string filter, Action scheduleChange)
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

            watcher.Changed += (_, _) => scheduleChange();
            watcher.Created += (_, _) => scheduleChange();
            watcher.Deleted += (_, _) => scheduleChange();
            watcher.Renamed += (_, _) => scheduleChange();
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
        DisposeWatcher(_indexWatcher);
        _headWatcher = null;
        _refWatcher = null;
        _packedRefsWatcher = null;
        _indexWatcher = null;
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

}

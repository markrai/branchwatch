using System.Threading;

namespace BranchWatch;

public sealed class GitRepositoryWatcher : IDisposable
{
    private readonly object _sync = new();
    private readonly System.Threading.Timer _refreshTimer;
    private readonly GitMetadataWatchers _metadataWatchers;
    private RepositoryInfo? _repository;
    private RepositoryStatus _currentStatus = RepositoryStatus.Empty;
    private bool _disposed;

    public event EventHandler<RepositoryStatus>? StatusChanged;

    public GitRepositoryWatcher()
    {
        _refreshTimer = new System.Threading.Timer(_ => Refresh(), null, Timeout.Infinite, Timeout.Infinite);
        _metadataWatchers = new GitMetadataWatchers(ScheduleRefresh);
    }

    public RepositoryStatus CurrentStatus
    {
        get
        {
            lock (_sync)
            {
                return _currentStatus;
            }
        }
    }

    public RepositorySelectionResult SelectRepository(string selectedPath)
    {
        if (!GitRepositoryResolver.TryResolveRepository(selectedPath, out var repository, out var error))
        {
            return RepositorySelectionResult.Failed(error);
        }

        RepositoryStatus status;
        lock (_sync)
        {
            ThrowIfDisposed();
            _repository = repository;
            status = GitBranchReader.ReadStatus(repository);
            _currentStatus = status;
            _metadataWatchers.Rebuild(repository, status);
        }

        OnStatusChanged(status);
        return RepositorySelectionResult.Succeeded(repository.RootPath);
    }

    public void Clear()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _repository = null;
            _currentStatus = RepositoryStatus.Empty;
            _metadataWatchers.Clear();
        }

        OnStatusChanged(RepositoryStatus.Empty);
    }

    public RepositoryStatus Refresh()
    {
        RepositoryStatus status;
        lock (_sync)
        {
            if (_disposed)
            {
                return _currentStatus;
            }

            if (_repository is null)
            {
                return _currentStatus;
            }

            status = GitBranchReader.ReadStatus(_repository);
            _currentStatus = status;
            _metadataWatchers.Rebuild(_repository, status);
        }

        OnStatusChanged(status);
        return status;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _refreshTimer.Dispose();
            _metadataWatchers.Dispose();
        }
    }

    private void ScheduleRefresh()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _refreshTimer.Change(150, Timeout.Infinite);
        }
    }

    private void OnStatusChanged(RepositoryStatus status)
    {
        StatusChanged?.Invoke(this, status);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(GitRepositoryWatcher));
        }
    }
}

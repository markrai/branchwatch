using System.Threading;

namespace BranchWatch;

internal sealed class WorkspaceRepositoryMonitor : IDisposable
{
    private readonly object _sync = new();
    private readonly WorkspaceRepositoryScanner _scanner = new();
    private readonly Dictionary<string, WorkspaceRepositoryState> _repositories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _pendingRefreshes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _pendingIndexActivities = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _pendingFileActivities = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingWatcherRecoveries = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Threading.Timer _refreshTimer;
    private readonly System.Threading.Timer _indexActivityTimer;
    private readonly System.Threading.Timer _fileActivityTimer;
    private readonly System.Threading.Timer _watcherRecoveryTimer;
    private long _activitySequence;
    private bool _disposed;

    public WorkspaceRepositoryMonitor()
    {
        _refreshTimer = new System.Threading.Timer(_ => RefreshPendingRepositories(), null, Timeout.Infinite, Timeout.Infinite);
        _indexActivityTimer = new System.Threading.Timer(_ => PromotePendingIndexActivities(), null, Timeout.Infinite, Timeout.Infinite);
        _fileActivityTimer = new System.Threading.Timer(_ => PromotePendingFileActivities(), null, Timeout.Infinite, Timeout.Infinite);
        _watcherRecoveryTimer = new System.Threading.Timer(_ => RecoverPendingWorkingTreeWatchers(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public event EventHandler<WorkspaceRepositoryChangedEventArgs>? RepositoryActivity;

    public IReadOnlyList<RepositoryStatus> Repositories
    {
        get
        {
            lock (_sync)
            {
                return _repositories.Values.Select(state => state.Status).ToArray();
            }
        }
    }

    public WorkspaceLoadResult Rescan(
        string workspaceRootPath,
        bool fileActivityEnabled = true,
        int maxDepth = 2)
    {
        var scanResult = _scanner.Scan(workspaceRootPath, maxDepth);
        if (!scanResult.Success)
        {
            Clear();
            return WorkspaceLoadResult.Failed(scanResult.ErrorMessage ?? "Unable to scan workspace.");
        }

        lock (_sync)
        {
            ThrowIfDisposed();
            ClearLocked();

            foreach (var repository in scanResult.Repositories)
            {
                var status = GitBranchReader.ReadStatus(repository);
                var state = new WorkspaceRepositoryState(
                    repository,
                    status,
                    ScheduleRefresh,
                    ScheduleIndexActivity,
                    ScheduleFileActivity,
                    ScheduleWorkingTreeWatcherRecovery,
                    fileActivityEnabled);
                state.MetadataWatchers.Rebuild(repository, status);
                _repositories.Add(repository.RootPath, state);
            }
        }

        return WorkspaceLoadResult.Succeeded(scanResult.WorkspaceRootPath!, Repositories);
    }

    public bool RefreshRepository(string repositoryRoot)
    {
        return RefreshRepository(repositoryRoot, NextActivitySequence());
    }

    private bool RefreshRepository(string repositoryRoot, long activitySequence)
    {
        WorkspaceRepositoryChangedEventArgs? changed = null;

        lock (_sync)
        {
            if (_disposed || !_repositories.TryGetValue(repositoryRoot, out var state))
            {
                return false;
            }

            var status = GitBranchReader.ReadStatus(state.Repository);
            var branchChanged = !string.Equals(state.Status.BranchDisplay, status.BranchDisplay, StringComparison.Ordinal);
            state.Status = status;
            state.MetadataWatchers.Rebuild(state.Repository, status);

            if (branchChanged)
            {
                changed = new WorkspaceRepositoryChangedEventArgs(
                    status,
                    WorkspaceActivityReason.BranchChanged,
                    activitySequence);
            }
        }

        if (changed is not null)
        {
            RepositoryActivity?.Invoke(this, changed);
            return true;
        }

        return false;
    }

    public bool PromoteRepositoryForFileActivity(string repositoryRoot)
    {
        return PromoteRepositoryForFileActivity(repositoryRoot, NextActivitySequence());
    }

    public bool PromoteRepositoryForIndexActivity(string repositoryRoot)
    {
        return PromoteRepositoryForIndexActivity(repositoryRoot, NextActivitySequence());
    }

    private bool PromoteRepositoryForIndexActivity(string repositoryRoot, long activitySequence)
    {
        WorkspaceRepositoryChangedEventArgs? changed = null;

        lock (_sync)
        {
            if (_disposed || !_repositories.TryGetValue(repositoryRoot, out var state))
            {
                return false;
            }

            var status = GitBranchReader.ReadStatus(state.Repository);
            state.Status = status;
            state.MetadataWatchers.Rebuild(state.Repository, status);
            changed = new WorkspaceRepositoryChangedEventArgs(
                status,
                WorkspaceActivityReason.IndexChanged,
                activitySequence);
        }

        RepositoryActivity?.Invoke(this, changed);
        return true;
    }

    private bool PromoteRepositoryForFileActivity(string repositoryRoot, long activitySequence)
    {
        WorkspaceRepositoryChangedEventArgs? changed = null;

        lock (_sync)
        {
            if (_disposed || !_repositories.TryGetValue(repositoryRoot, out var state) || !state.FileActivityEnabled)
            {
                return false;
            }

            var status = GitBranchReader.ReadStatus(state.Repository);
            state.Status = status;
            state.MetadataWatchers.Rebuild(state.Repository, status);
            changed = new WorkspaceRepositoryChangedEventArgs(
                status,
                WorkspaceActivityReason.FileChanged,
                activitySequence);
        }

        RepositoryActivity?.Invoke(this, changed);
        return true;
    }

    public void Clear()
    {
        lock (_sync)
        {
            ClearLocked();
        }
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
            _indexActivityTimer.Dispose();
            _fileActivityTimer.Dispose();
            _watcherRecoveryTimer.Dispose();
            ClearLocked();
        }
    }

    private void ScheduleRefresh(string repositoryRoot)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _pendingRefreshes[repositoryRoot] = ++_activitySequence;
            _refreshTimer.Change(150, Timeout.Infinite);
        }
    }

    private void ScheduleFileActivity(string repositoryRoot)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _pendingFileActivities[repositoryRoot] = ++_activitySequence;
            _fileActivityTimer.Change(300, Timeout.Infinite);
        }
    }

    private void ScheduleIndexActivity(string repositoryRoot)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _pendingIndexActivities[repositoryRoot] = ++_activitySequence;
            _indexActivityTimer.Change(150, Timeout.Infinite);
        }
    }

    private void ScheduleWorkingTreeWatcherRecovery(string repositoryRoot)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _pendingWatcherRecoveries.Add(repositoryRoot);
            _watcherRecoveryTimer.Change(1000, Timeout.Infinite);
        }
    }

    private void RefreshPendingRepositories()
    {
        KeyValuePair<string, long>[] pending;
        lock (_sync)
        {
            pending = _pendingRefreshes.ToArray();
            _pendingRefreshes.Clear();
        }

        foreach (var pendingRefresh in pending.OrderBy(item => item.Value))
        {
            RefreshRepository(pendingRefresh.Key, pendingRefresh.Value);
        }
    }

    private void PromotePendingIndexActivities()
    {
        KeyValuePair<string, long>[] pending;
        lock (_sync)
        {
            pending = _pendingIndexActivities.ToArray();
            _pendingIndexActivities.Clear();
        }

        foreach (var pendingActivity in pending.OrderBy(item => item.Value))
        {
            PromoteRepositoryForIndexActivity(pendingActivity.Key, pendingActivity.Value);
        }
    }

    private void PromotePendingFileActivities()
    {
        KeyValuePair<string, long>[] pending;
        lock (_sync)
        {
            pending = _pendingFileActivities.ToArray();
            _pendingFileActivities.Clear();
        }

        foreach (var pendingActivity in pending.OrderBy(item => item.Value))
        {
            PromoteRepositoryForFileActivity(pendingActivity.Key, pendingActivity.Value);
        }
    }

    private void RecoverPendingWorkingTreeWatchers()
    {
        string[] pending;
        lock (_sync)
        {
            pending = _pendingWatcherRecoveries.ToArray();
            _pendingWatcherRecoveries.Clear();

            foreach (var repositoryRoot in pending)
            {
                if (_repositories.TryGetValue(repositoryRoot, out var state))
                {
                    state.RebuildWorkingTreeWatcher();
                }
            }
        }
    }

    private long NextActivitySequence()
    {
        lock (_sync)
        {
            return ++_activitySequence;
        }
    }

    private void ClearLocked()
    {
        foreach (var repository in _repositories.Values)
        {
            repository.MetadataWatchers.Dispose();
            repository.WorkingTreeWatcher?.Dispose();
        }

        _repositories.Clear();
        _pendingRefreshes.Clear();
        _pendingIndexActivities.Clear();
        _pendingFileActivities.Clear();
        _pendingWatcherRecoveries.Clear();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WorkspaceRepositoryMonitor));
        }
    }

    private sealed class WorkspaceRepositoryState
    {
        public WorkspaceRepositoryState(
            RepositoryInfo repository,
            RepositoryStatus status,
            Action<string> scheduleRefresh,
            Action<string> scheduleIndexActivity,
            Action<string> scheduleFileActivity,
            Action<string> scheduleWatcherRecovery,
            bool fileActivityEnabled)
        {
            Repository = repository;
            Status = status;
            FileActivityEnabled = fileActivityEnabled;
            MetadataWatchers = new GitMetadataWatchers(
                () => scheduleRefresh(repository.RootPath),
                () => scheduleIndexActivity(repository.RootPath));
            ScheduleFileActivity = scheduleFileActivity;
            ScheduleWatcherRecovery = scheduleWatcherRecovery;
            RebuildWorkingTreeWatcher();
        }

        public RepositoryInfo Repository { get; }

        public RepositoryStatus Status { get; set; }

        public bool FileActivityEnabled { get; }

        public GitMetadataWatchers MetadataWatchers { get; }

        public WorkspaceWorkingTreeWatcher? WorkingTreeWatcher { get; private set; }

        private Action<string> ScheduleFileActivity { get; }

        private Action<string> ScheduleWatcherRecovery { get; }

        public void RebuildWorkingTreeWatcher()
        {
            WorkingTreeWatcher?.Dispose();
            WorkingTreeWatcher = FileActivityEnabled
                ? new WorkspaceWorkingTreeWatcher(
                    Repository.RootPath,
                    () => ScheduleFileActivity(Repository.RootPath),
                    () => ScheduleWatcherRecovery(Repository.RootPath))
                : null;
        }
    }
}

internal sealed class WorkspaceRepositoryChangedEventArgs : EventArgs
{
    public WorkspaceRepositoryChangedEventArgs(
        RepositoryStatus status,
        WorkspaceActivityReason reason,
        long activitySequence)
    {
        Status = status;
        Reason = reason;
        ActivitySequence = activitySequence;
    }

    public RepositoryStatus Status { get; }

    public WorkspaceActivityReason Reason { get; }

    public long ActivitySequence { get; }
}

namespace BranchWatch;

public sealed class RepositorySessionController : IDisposable
{
    public const string NoRepositoriesFoundStatus = "No Git repositories found under workspace.";
    public const string NoWorkspaceConfiguredStatus = "No workspace folder is configured.";
    public const string WaitingForWorkspaceActivityStatus = "Waiting for workspace activity.";

    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly GitRepositoryWatcher _repositoryWatcher;
    private readonly WorkspaceRepositoryMonitor _workspaceMonitor = new();
    private long _lastWorkspaceActivitySequence;
    private bool _disposed;

    public event EventHandler? StateChanged;

    public RepositorySessionController(
        SettingsService settingsService,
        AppSettings settings,
        GitRepositoryWatcher repositoryWatcher)
    {
        _settingsService = settingsService;
        _settings = settings;
        _repositoryWatcher = repositoryWatcher;
        _workspaceMonitor.RepositoryActivity += OnWorkspaceRepositoryActivity;
    }

    public RepositoryWatchMode WatchMode => _settings.WatchMode;

    public string? WorkspaceStatusText { get; private set; }

    public WorkspaceActivityReason? LastWorkspaceActivityReason { get; private set; }

    public IReadOnlyList<RepositoryStatus> WorkspaceRepositories => _workspaceMonitor.Repositories;

    public RepositorySelectionResult LoadPinnedRepository()
    {
        if (string.IsNullOrWhiteSpace(_settings.WatchedRepositoryPath))
        {
            _repositoryWatcher.Clear();
            WorkspaceStatusText = null;
            LastWorkspaceActivityReason = null;
            _lastWorkspaceActivitySequence = 0;
            return RepositorySelectionResult.Failed("No pinned repository is configured.");
        }

        return SelectRepository(_settings.WatchedRepositoryPath, persistPinnedPath: false);
    }

    public RepositorySelectionResult SelectPinnedRepository(string path)
    {
        _settings.WatchMode = RepositoryWatchMode.PinnedRepo;
        _settingsService.Save(_settings);
        StopWorkspace();
        return SelectRepository(path, persistPinnedPath: true);
    }

    public WorkspaceLoadResult LoadWorkspace()
    {
        if (string.IsNullOrWhiteSpace(_settings.WorkspaceRootPath))
        {
            _repositoryWatcher.Clear();
            WorkspaceStatusText = NoWorkspaceConfiguredStatus;
            LastWorkspaceActivityReason = null;
            _lastWorkspaceActivitySequence = 0;
            OnStateChanged();
            return WorkspaceLoadResult.Failed(NoWorkspaceConfiguredStatus);
        }

        return StartWorkspace(_settings.WorkspaceRootPath, persistWorkspacePath: false, WorkspaceActivityReason.WorkspaceLoaded);
    }

    public WorkspaceLoadResult SelectWorkspace(string path)
    {
        var previousMode = _settings.WatchMode;
        var result = StartWorkspace(path, persistWorkspacePath: false, WorkspaceActivityReason.WorkspaceLoaded);
        if (result.Success)
        {
            _settings.WatchMode = RepositoryWatchMode.WorkspaceRepo;
            _settings.WorkspaceRootPath = result.WorkspaceRootPath;
            _settingsService.Save(_settings);
        }
        else
        {
            _settings.WatchMode = previousMode;
        }

        return result;
    }

    public WorkspaceLoadResult RescanWorkspace()
    {
        if (_settings.WatchMode != RepositoryWatchMode.WorkspaceRepo)
        {
            return WorkspaceLoadResult.Failed("BranchWatch is not in WorkspaceRepo mode.");
        }

        if (string.IsNullOrWhiteSpace(_settings.WorkspaceRootPath))
        {
            _repositoryWatcher.Clear();
            WorkspaceStatusText = NoWorkspaceConfiguredStatus;
            LastWorkspaceActivityReason = null;
            _lastWorkspaceActivitySequence = 0;
            OnStateChanged();
            return WorkspaceLoadResult.Failed(NoWorkspaceConfiguredStatus);
        }

        return StartWorkspace(_settings.WorkspaceRootPath, persistWorkspacePath: false, WorkspaceActivityReason.WorkspaceRescanned);
    }

    public void SetWatchMode(RepositoryWatchMode watchMode)
    {
        if (_settings.WatchMode == watchMode)
        {
            return;
        }

        _settings.WatchMode = watchMode;
        _settingsService.Save(_settings);

        if (watchMode == RepositoryWatchMode.PinnedRepo)
        {
            StopWorkspace();
            if (!string.IsNullOrWhiteSpace(_settings.WatchedRepositoryPath))
            {
                SelectRepository(_settings.WatchedRepositoryPath, persistPinnedPath: false);
            }
            else
            {
                _repositoryWatcher.Clear();
                OnStateChanged();
            }
        }
        else
        {
            LoadWorkspace();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _workspaceMonitor.RepositoryActivity -= OnWorkspaceRepositoryActivity;
        _workspaceMonitor.Dispose();
    }

    internal bool RefreshWorkspaceRepository(string repositoryRoot)
    {
        return _workspaceMonitor.RefreshRepository(repositoryRoot);
    }

    internal bool PromoteWorkspaceRepositoryForIndexActivity(string repositoryRoot)
    {
        return _workspaceMonitor.PromoteRepositoryForIndexActivity(repositoryRoot);
    }

    internal bool PromoteWorkspaceRepositoryForFileActivity(string repositoryRoot, string changedPath)
    {
        if (!_settings.WorkspaceFileActivityEnabled
            || !WorkspaceWorkingTreeWatcher.IsWorkingTreeActivityPath(repositoryRoot, changedPath))
        {
            return false;
        }

        return _workspaceMonitor.PromoteRepositoryForFileActivity(repositoryRoot);
    }

    internal bool PromoteWorkspaceRepositoryForRepoOpened(string repositoryRoot)
    {
        if (_settings.WatchMode != RepositoryWatchMode.WorkspaceRepo)
        {
            return false;
        }

        return _workspaceMonitor.PromoteRepositoryForRepoOpened(repositoryRoot);
    }

    public WorkspaceActivityReportResult TryReportRepoOpened(string path)
    {
        if (_settings.WatchMode != RepositoryWatchMode.WorkspaceRepo)
        {
            return WorkspaceActivityReportResult.Ignored(
                "BranchWatch is not in WorkspaceRepo mode. Repo-opened activity applies only in WorkspaceRepo mode.");
        }

        if (!GitRepositoryResolver.TryResolveRepository(path, out var repository, out var error))
        {
            return WorkspaceActivityReportResult.Failed(error);
        }

        if (!_workspaceMonitor.PromoteRepositoryForRepoOpened(repository.RootPath))
        {
            return WorkspaceActivityReportResult.Ignored(
                $"Repository is not in the current workspace: {repository.RootPath}. Try Rescan workspace or increase WorkspaceDiscoveryMaxDepth in settings.json.");
        }

        return WorkspaceActivityReportResult.ForPromotion(repository.RootPath);
    }

    private WorkspaceLoadResult StartWorkspace(
        string path,
        bool persistWorkspacePath,
        WorkspaceActivityReason activityReason)
    {
        var result = _workspaceMonitor.Rescan(
            path,
            _settings.WorkspaceFileActivityEnabled,
            _settings.WorkspaceDiscoveryMaxDepth);
        if (!result.Success)
        {
            _repositoryWatcher.Clear();
            WorkspaceStatusText = result.ErrorMessage;
            LastWorkspaceActivityReason = null;
            _lastWorkspaceActivitySequence = 0;
            OnStateChanged();
            return result;
        }

        if (persistWorkspacePath)
        {
            _settings.WorkspaceRootPath = result.WorkspaceRootPath;
            _settingsService.Save(_settings);
        }

        if (result.Repositories.Count == 0)
        {
            _repositoryWatcher.Clear();
            WorkspaceStatusText = NoRepositoriesFoundStatus;
            LastWorkspaceActivityReason = activityReason;
            _lastWorkspaceActivitySequence = 0;
            OnStateChanged();
            return result with { ErrorMessage = NoRepositoriesFoundStatus };
        }

        WorkspaceStatusText = null;
        LastWorkspaceActivityReason = activityReason;
        _lastWorkspaceActivitySequence = 0;
        var selected = SelectInitialWorkspaceRepository(result.Repositories);
        if (selected is null)
        {
            _repositoryWatcher.Clear();
            WorkspaceStatusText = WaitingForWorkspaceActivityStatus;
            OnStateChanged();
            return result;
        }

        _repositoryWatcher.SelectRepository(selected.RepositoryRoot!);
        OnStateChanged();
        return result;
    }

    private RepositoryStatus? SelectInitialWorkspaceRepository(IReadOnlyList<RepositoryStatus> repositories)
    {
        var lastActive = FindRepository(repositories, _settings.LastActiveWorkspaceRepositoryPath);
        if (lastActive is not null)
        {
            return lastActive;
        }

        return null;
    }

    private static RepositoryStatus? FindRepository(
        IReadOnlyList<RepositoryStatus> repositories,
        string? repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot))
        {
            return null;
        }

        return repositories.FirstOrDefault(repository =>
            string.Equals(repository.RepositoryRoot, repositoryRoot, StringComparison.OrdinalIgnoreCase));
    }

    private RepositorySelectionResult SelectRepository(string path, bool persistPinnedPath)
    {
        var result = _repositoryWatcher.SelectRepository(path);
        if (result.Success && persistPinnedPath)
        {
            _settings.WatchedRepositoryPath = result.RepositoryRoot;
            _settingsService.Save(_settings);
        }

        WorkspaceStatusText = null;
        LastWorkspaceActivityReason = null;
        _lastWorkspaceActivitySequence = 0;
        OnStateChanged();
        return result;
    }

    private void StopWorkspace()
    {
        _workspaceMonitor.Clear();
        WorkspaceStatusText = null;
        LastWorkspaceActivityReason = null;
        _lastWorkspaceActivitySequence = 0;
    }

    private void OnWorkspaceRepositoryActivity(object? sender, WorkspaceRepositoryChangedEventArgs e)
    {
        if (_settings.WatchMode != RepositoryWatchMode.WorkspaceRepo)
        {
            return;
        }

        if (e.ActivitySequence < _lastWorkspaceActivitySequence)
        {
            return;
        }

        PersistLastActiveWorkspaceRepository(e.Status.RepositoryRoot);

        var currentStatus = _repositoryWatcher.CurrentStatus;
        var sameActiveRepository = string.Equals(
            currentStatus.RepositoryRoot,
            e.Status.RepositoryRoot,
            StringComparison.OrdinalIgnoreCase);
        var sameDisplayedBranch = string.Equals(
            currentStatus.BranchDisplay,
            e.Status.BranchDisplay,
            StringComparison.Ordinal);
        var sameActivityReason = LastWorkspaceActivityReason == e.Reason;

        if (sameActiveRepository && sameDisplayedBranch && sameActivityReason && string.IsNullOrWhiteSpace(WorkspaceStatusText)
            && e.Reason != WorkspaceActivityReason.RepoOpened)
        {
            _lastWorkspaceActivitySequence = e.ActivitySequence;
            return;
        }

        WorkspaceStatusText = null;
        LastWorkspaceActivityReason = e.Reason;
        _lastWorkspaceActivitySequence = e.ActivitySequence;
        if (sameActiveRepository)
        {
            if (e.Reason == WorkspaceActivityReason.RepoOpened || !sameDisplayedBranch)
            {
                _repositoryWatcher.Refresh();
            }
        }
        else
        {
            _repositoryWatcher.SelectRepository(e.Status.RepositoryRoot!);
        }

        OnStateChanged();
    }

    private void PersistLastActiveWorkspaceRepository(string? repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot)
            || string.Equals(_settings.LastActiveWorkspaceRepositoryPath, repositoryRoot, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _settings.LastActiveWorkspaceRepositoryPath = repositoryRoot;
        _settingsService.Save(_settings);
    }

    private void OnStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}

public sealed record WorkspaceLoadResult(
    bool Success,
    string? WorkspaceRootPath,
    IReadOnlyList<RepositoryStatus> Repositories,
    string? ErrorMessage)
{
    public static WorkspaceLoadResult Succeeded(string workspaceRootPath, IReadOnlyList<RepositoryStatus> repositories) =>
        new(true, workspaceRootPath, repositories, null);

    public static WorkspaceLoadResult Failed(string errorMessage) =>
        new(false, null, Array.Empty<RepositoryStatus>(), errorMessage);
}

public sealed record WorkspaceActivityReportResult(bool Success, bool Promoted, string Message)
{
    public static WorkspaceActivityReportResult ForPromotion(string repositoryRoot) =>
        new(true, true, $"Promoted repository: {repositoryRoot}");

    public static WorkspaceActivityReportResult Ignored(string message) =>
        new(true, false, message);

    public static WorkspaceActivityReportResult Failed(string message) =>
        new(false, false, message);
}

using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace BranchWatch;

public sealed class TrayService : IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly StartupService _startupService;
    private readonly RepositorySessionController _sessionController;
    private readonly GitRepositoryWatcher _repositoryWatcher;
    private readonly OverlayWindow _overlayWindow;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.Form _dialogOwner;
    private PersonalizeWindow? _personalizeWindow;
    private Forms.ToolStripMenuItem? _modeItem;
    private Forms.ToolStripMenuItem? _repositoryItem;
    private Forms.ToolStripMenuItem? _activeRepositoryItem;
    private Forms.ToolStripMenuItem? _branchItem;
    private Forms.ToolStripMenuItem? _lastActivityItem;
    private Forms.ToolStripMenuItem? _statusItem;
    private Forms.ToolStripMenuItem? _rescanWorkspaceItem;
    private Forms.ToolStripMenuItem? _refreshItem;
    private Forms.ToolStripMenuItem? _overlayToggleItem;
    private Forms.ToolStripMenuItem? _pinnedRepoModeItem;
    private Forms.ToolStripMenuItem? _workspaceRepoModeItem;
    private Forms.ToolStripMenuItem? _startupItem;
    private bool _disposed;

    public TrayService(
        SettingsService settingsService,
        AppSettings settings,
        StartupService startupService,
        RepositorySessionController sessionController,
        GitRepositoryWatcher repositoryWatcher,
        OverlayWindow overlayWindow)
    {
        _settingsService = settingsService;
        _settings = settings;
        _startupService = startupService;
        _sessionController = sessionController;
        _repositoryWatcher = repositoryWatcher;
        _overlayWindow = overlayWindow;
        _dialogOwner = CreateDialogOwner();
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "BranchWatch",
            Visible = false,
            ContextMenuStrip = BuildContextMenu()
        };

        _notifyIcon.DoubleClick += (_, _) => ToggleOverlay();
        _sessionController.StateChanged += OnSessionStateChanged;
        _repositoryWatcher.StatusChanged += OnRepositoryStatusChanged;
    }

    public void Start()
    {
        _notifyIcon.Visible = true;
        ApplyOverlayState();
    }

    public void SelectRepository(string path, bool showErrors)
    {
        var result = _sessionController.SelectPinnedRepository(path);
        if (!result.Success)
        {
            if (showErrors)
            {
                ShowError(result.ErrorMessage ?? "Unable to select repository.");
            }
            else
            {
                ShowBalloon("Repository unavailable", result.ErrorMessage ?? "Unable to read the saved repository path.");
            }

            ApplyOverlayState();
            return;
        }

        ApplyOverlayState();
    }

    public void LoadPinnedRepository()
    {
        if (string.IsNullOrWhiteSpace(_settings.WatchedRepositoryPath))
        {
            ApplyOverlayState();
            return;
        }

        var result = _sessionController.LoadPinnedRepository();
        if (!result.Success)
        {
            ShowBalloon("Repository unavailable", result.ErrorMessage ?? "Unable to read the saved repository path.");
        }

        ApplyOverlayState();
    }

    public void LoadWorkspace()
    {
        var result = _sessionController.LoadWorkspace();
        if (!result.Success)
        {
            ShowBalloon("Workspace unavailable", result.ErrorMessage ?? "Unable to load workspace.");
        }

        ApplyOverlayState();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _personalizeWindow?.Close();
        _sessionController.StateChanged -= OnSessionStateChanged;
        _repositoryWatcher.StatusChanged -= OnRepositoryStatusChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _dialogOwner.Dispose();
    }

    private Forms.ContextMenuStrip BuildContextMenu()
    {
        var menu = new Forms.ContextMenuStrip();

        _modeItem = CreateDisabledItem("Mode: Pinned Repo Mode");
        _repositoryItem = CreateDisabledItem("Pinned repo: (none)");
        _activeRepositoryItem = CreateDisabledItem("Active repo: (none)");
        _activeRepositoryItem.Visible = false;
        _branchItem = CreateDisabledItem("Branch: No repository selected");
        _lastActivityItem = CreateDisabledItem("Last activity: (none)");
        _lastActivityItem.Visible = false;
        _statusItem = CreateDisabledItem(string.Empty);
        _statusItem.Visible = false;

        menu.Items.Add(_modeItem);
        menu.Items.Add(_repositoryItem);
        menu.Items.Add(_activeRepositoryItem);
        menu.Items.Add(_branchItem);
        menu.Items.Add(_lastActivityItem);
        menu.Items.Add(_statusItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(CreateItem("Choose repository...", (_, _) => RunAfterContextMenuClosed(ChooseRepository)));
        menu.Items.Add(CreateItem("Choose workspace...", (_, _) => RunAfterContextMenuClosed(ChooseWorkspace)));
        _rescanWorkspaceItem = CreateItem("Rescan workspace", (_, _) => RescanWorkspace());
        menu.Items.Add(_rescanWorkspaceItem);
        menu.Items.Add(CreateItem("Personalize...", (_, _) => OpenPersonalize()));

        _refreshItem = CreateItem("Refresh branch", (_, _) => RefreshBranch());
        menu.Items.Add(_refreshItem);

        _overlayToggleItem = CreateItem("Hide overlay", (_, _) => ToggleOverlay());
        menu.Items.Add(_overlayToggleItem);

        _pinnedRepoModeItem = CreateItem("Pinned Repo Mode", (_, _) => SetPinnedRepoMode());
        _pinnedRepoModeItem.CheckOnClick = false;
        menu.Items.Add(_pinnedRepoModeItem);

        _workspaceRepoModeItem = CreateItem("Workspace Mode", (_, _) => SetWorkspaceRepoMode());
        _workspaceRepoModeItem.CheckOnClick = false;
        menu.Items.Add(_workspaceRepoModeItem);

        _startupItem = CreateItem("Start with Windows", (_, _) => ToggleStartWithWindows());
        _startupItem.CheckOnClick = false;
        menu.Items.Add(_startupItem);

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(CreateItem("Exit", (_, _) => ExitApplication()));

        menu.Opening += (_, _) => RefreshContextMenu();
        RefreshContextMenu();

        return menu;
    }

    private void RefreshContextMenu()
    {
        if (_modeItem is null || _repositoryItem is null || _activeRepositoryItem is null
            || _branchItem is null || _lastActivityItem is null || _statusItem is null || _rescanWorkspaceItem is null
            || _refreshItem is null || _overlayToggleItem is null || _pinnedRepoModeItem is null
            || _workspaceRepoModeItem is null || _startupItem is null)
        {
            return;
        }

        var status = _repositoryWatcher.CurrentStatus;
        var isWorkspaceRepoMode = _settings.WatchMode == RepositoryWatchMode.WorkspaceRepo;

        _modeItem.Text = $"Mode: {FormatWatchModeLabel(_settings.WatchMode)}";
        _repositoryItem.Text = isWorkspaceRepoMode
            ? $"Workspace: {FormatRepositoryLabel(_settings.WorkspaceRootPath)}"
            : $"Pinned repo: {FormatRepositoryLabel(status.RepositoryRoot)}";
        _activeRepositoryItem.Text = $"Active repo: {FormatRepositoryLabel(status.RepositoryRoot)}";
        _activeRepositoryItem.Visible = isWorkspaceRepoMode;
        _branchItem.Text = $"Branch: {status.BranchDisplay}";
        _lastActivityItem.Text = $"Last activity: {FormatWorkspaceActivityReason(_sessionController.LastWorkspaceActivityReason)}";
        _lastActivityItem.Visible = isWorkspaceRepoMode && _sessionController.LastWorkspaceActivityReason.HasValue;

        if (!string.IsNullOrWhiteSpace(status.ErrorMessage))
        {
            _statusItem.Text = $"Status: {status.ErrorMessage}";
            _statusItem.Visible = true;
        }
        else if (!string.IsNullOrWhiteSpace(_sessionController.WorkspaceStatusText))
        {
            _statusItem.Text = $"Status: {_sessionController.WorkspaceStatusText}";
            _statusItem.Visible = true;
        }
        else
        {
            _statusItem.Visible = false;
        }

        _rescanWorkspaceItem.Enabled = isWorkspaceRepoMode && !string.IsNullOrWhiteSpace(_settings.WorkspaceRootPath);
        _refreshItem.Enabled = !string.IsNullOrWhiteSpace(status.RepositoryRoot);
        _overlayToggleItem.Text = _settings.OverlayVisible ? "Hide overlay" : "Show overlay";
        _pinnedRepoModeItem.Checked = _settings.WatchMode == RepositoryWatchMode.PinnedRepo;
        _workspaceRepoModeItem.Checked = isWorkspaceRepoMode;
        _startupItem.Checked = _settings.StartWithWindows;
    }

    private static Forms.ToolStripMenuItem CreateDisabledItem(string text)
    {
        return new Forms.ToolStripMenuItem(text)
        {
            Enabled = false
        };
    }

    private static Forms.ToolStripMenuItem CreateItem(string text, EventHandler onClick)
    {
        var item = new Forms.ToolStripMenuItem(text);
        item.Click += onClick;
        return item;
    }

    private void ChooseRepository()
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Choose a folder inside the Git repository to watch.",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (!string.IsNullOrWhiteSpace(_settings.WatchedRepositoryPath) && Directory.Exists(_settings.WatchedRepositoryPath))
        {
            dialog.SelectedPath = _settings.WatchedRepositoryPath;
        }

        if (dialog.ShowDialog(_dialogOwner) == Forms.DialogResult.OK)
        {
            SelectRepository(dialog.SelectedPath, showErrors: true);
        }
    }

    private void ChooseWorkspace()
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Choose the parent folder containing Git repositories.",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (!string.IsNullOrWhiteSpace(_settings.WorkspaceRootPath) && Directory.Exists(_settings.WorkspaceRootPath))
        {
            dialog.SelectedPath = _settings.WorkspaceRootPath;
        }

        if (dialog.ShowDialog(_dialogOwner) == Forms.DialogResult.OK)
        {
            var result = _sessionController.SelectWorkspace(dialog.SelectedPath);
            if (!result.Success)
            {
                ShowError(result.ErrorMessage ?? "Unable to load workspace.");
            }

            ApplyOverlayState();
        }
    }

    private void RescanWorkspace()
    {
        var result = _sessionController.RescanWorkspace();
        if (!result.Success)
        {
            ShowBalloon("Workspace unavailable", result.ErrorMessage ?? "Unable to rescan workspace.");
        }

        ApplyOverlayState();
    }

    private void OpenPersonalize()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (_personalizeWindow is null)
            {
                _personalizeWindow = new PersonalizeWindow(_settingsService, _settings, ApplyOverlayState);
                _personalizeWindow.Closed += (_, _) => _personalizeWindow = null;
                _personalizeWindow.Show();
            }
            else
            {
                _personalizeWindow.Activate();
                _personalizeWindow.Focus();
            }
        });
    }

    private void RefreshBranch()
    {
        _repositoryWatcher.Refresh();
        ApplyOverlayState();
    }

    private void ToggleOverlay()
    {
        _settings.OverlayVisible = !_settings.OverlayVisible;
        _settingsService.Save(_settings);
        ApplyOverlayState();
    }

    private void SetPinnedRepoMode()
    {
        _sessionController.SetWatchMode(RepositoryWatchMode.PinnedRepo);
        ApplyOverlayState();
    }

    private void SetWorkspaceRepoMode()
    {
        _sessionController.SetWatchMode(RepositoryWatchMode.WorkspaceRepo);
        ApplyOverlayState();
    }

    private void ToggleStartWithWindows()
    {
        var enabled = !_settings.StartWithWindows;
        try
        {
            _startupService.SetEnabled(enabled);
            _settings.StartWithWindows = enabled;
            _settingsService.Save(_settings);
        }
        catch (Exception ex)
        {
            ShowError($"Unable to update Windows startup setting:\n{ex.Message}");
        }
    }

    private void ExitApplication()
    {
        _overlayWindow.Hide();
        Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    private void OnRepositoryStatusChanged(object? sender, RepositoryStatus status)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(ApplyOverlayState));
    }

    private void OnSessionStateChanged(object? sender, EventArgs e)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(ApplyOverlayState));
    }

    private void ApplyOverlayState()
    {
        var status = _repositoryWatcher.CurrentStatus;
        UpdateNotifyIcon(status);

        if (_settings.OverlayVisible && !string.IsNullOrWhiteSpace(status.RepositoryRoot))
        {
            var activityReason = WorkspaceActivityReasonFormatter.FormatForOverlay(
                _settings,
                _sessionController.LastWorkspaceActivityReason);
            _overlayWindow.SetOverlayText(status.BranchDisplay, status.RepositoryRoot, activityReason);
            _overlayWindow.ShowOverlay(_settings);
        }
        else
        {
            _overlayWindow.Hide();
        }

        RefreshContextMenu();
    }

    private void UpdateNotifyIcon(RepositoryStatus status)
    {
        var text = string.IsNullOrWhiteSpace(status.RepositoryRoot)
            ? "BranchWatch"
            : $"BranchWatch - {status.BranchDisplay}";

        _notifyIcon.Text = text.Length <= 63 ? text : text[..60] + "...";
    }

    private static string FormatRepositoryLabel(string? repositoryRoot)
    {
        return string.IsNullOrWhiteSpace(repositoryRoot) ? "(none)" : repositoryRoot;
    }

    private static string FormatWatchModeLabel(RepositoryWatchMode watchMode)
    {
        return watchMode == RepositoryWatchMode.WorkspaceRepo ? "Workspace Mode" : "Pinned Repo Mode";
    }

    private static string FormatWorkspaceActivityReason(WorkspaceActivityReason? reason)
    {
        return WorkspaceActivityReasonFormatter.FormatOrNone(reason);
    }

    private static Forms.Form CreateDialogOwner()
    {
        return new Forms.Form
        {
            ShowInTaskbar = false,
            FormBorderStyle = Forms.FormBorderStyle.None,
            Size = new System.Drawing.Size(0, 0),
            StartPosition = Forms.FormStartPosition.Manual,
            Location = new System.Drawing.Point(-32000, -32000)
        };
    }

    private void RunAfterContextMenuClosed(Action action)
    {
        _notifyIcon.ContextMenuStrip?.Close();
        System.Windows.Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, action);
    }

    private void ShowError(string message)
    {
        Forms.MessageBox.Show(_dialogOwner, message, "BranchWatch", Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Error);
    }

    private void ShowBalloon(string title, string message)
    {
        if (_notifyIcon.Visible)
        {
            _notifyIcon.ShowBalloonTip(5000, title, message, Forms.ToolTipIcon.Warning);
        }
    }
}

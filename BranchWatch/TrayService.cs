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
    private readonly VirtualDesktopOverlayController _virtualDesktopOverlayController;
    private readonly StickyNotesController _stickyNotes;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Icon _trayIcon;
    private readonly Forms.Form _dialogOwner;
    private readonly List<Image> _menuImages = [];
    private SettingsWindow? _settingsWindow;
    private Forms.ToolStripMenuItem? _desktopOverlayToggleItem;
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
    private bool _disposed;

    public TrayService(
        SettingsService settingsService,
        AppSettings settings,
        StartupService startupService,
        RepositorySessionController sessionController,
        GitRepositoryWatcher repositoryWatcher,
        OverlayWindow overlayWindow,
        VirtualDesktopOverlayController virtualDesktopOverlayController,
        StickyNotesController stickyNotes)
    {
        _settingsService = settingsService;
        _settings = settings;
        _startupService = startupService;
        _sessionController = sessionController;
        _repositoryWatcher = repositoryWatcher;
        _overlayWindow = overlayWindow;
        _virtualDesktopOverlayController = virtualDesktopOverlayController;
        _stickyNotes = stickyNotes;
        _dialogOwner = CreateDialogOwner();
        _trayIcon = ApplicationIcon.LoadTrayIcon();
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _trayIcon,
            Text = "BranchWatch",
            Visible = false,
            ContextMenuStrip = BuildContextMenu()
        };

        _stickyNotes.Error += OnStickyNotesError;
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
        _stickyNotes.Error -= OnStickyNotesError;
        _settingsWindow?.Close();
        _sessionController.StateChanged -= OnSessionStateChanged;
        _repositoryWatcher.StatusChanged -= OnRepositoryStatusChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        foreach (var image in _menuImages)
        {
            image.Dispose();
        }
        _menuImages.Clear();
        _trayIcon.Dispose();
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

        var title = CreateDisabledItem("BranchWatch");
        title.Image = _trayIcon.ToBitmap();
        _menuImages.Add(title.Image);
        menu.Items.Add(title);
        menu.Items.Add(_modeItem);
        menu.Items.Add(_activeRepositoryItem);
        menu.Items.Add(_branchItem);
        menu.Items.Add(_statusItem);
        menu.Items.Add(new Forms.ToolStripSeparator());

        var git = new Forms.ToolStripMenuItem("Git Watcher") { Image = MenuIcon(TrayMenuGlyph.Git) };
        _overlayToggleItem = CreateItem("Hide overlay", (_, _) => ToggleOverlay());
        _refreshItem = CreateItem("Refresh branch", (_, _) => RefreshBranch());
        _rescanWorkspaceItem = CreateItem("Rescan workspace", (_, _) => RescanWorkspace());
        git.DropDownItems.AddRange([_overlayToggleItem, _refreshItem, _rescanWorkspaceItem,
            new Forms.ToolStripSeparator(),
            CreateItem("Choose repository...", (_, _) => RunAfterContextMenuClosed(ChooseRepository)),
            CreateItem("Choose workspace...", (_, _) => RunAfterContextMenuClosed(ChooseWorkspace))]);
        var mode = new Forms.ToolStripMenuItem("Watch mode");
        _pinnedRepoModeItem = CreateItem("Pinned repository", (_, _) => SetPinnedRepoMode());
        _workspaceRepoModeItem = CreateItem("Workspace", (_, _) => SetWorkspaceRepoMode());
        mode.DropDownItems.AddRange([_pinnedRepoModeItem, _workspaceRepoModeItem]);
        git.DropDownItems.Add(mode);
        git.DropDownItems.Add(_repositoryItem);
        git.DropDownItems.Add(_lastActivityItem);
        git.DropDownItems.Add(new Forms.ToolStripSeparator());
        git.DropDownItems.Add(CreateItem("Preferences...", (_, _) => OpenSettings(SettingsSection.GitWatcher)));

        var desktops = new Forms.ToolStripMenuItem("Virtual Desktops") { Image = MenuIcon(TrayMenuGlyph.Desktops) };
        _desktopOverlayToggleItem = CreateItem("Hide overlay", (_, _) => ToggleDesktopOverlay());
        desktops.DropDownItems.Add(_desktopOverlayToggleItem);
        desktops.DropDownItems.Add(CreateItem("Desktop Memory...", (_, _) => OpenSettings(SettingsSection.DesktopMemory)));
        desktops.DropDownItems.Add(CreateItem("Preferences...", (_, _) => OpenSettings(SettingsSection.VirtualDesktops)));

        var settings = CreateItem("Settings...", (_, _) => OpenSettings(SettingsSection.General));
        settings.Image = MenuIcon(TrayMenuGlyph.Settings);
        var about = CreateItem("About BranchWatch", (_, _) => OpenSettings(SettingsSection.About));
        about.Image = MenuIcon(TrayMenuGlyph.About);
        var exit = CreateItem("Exit", (_, _) => ExitApplication());
        exit.Image = MenuIcon(TrayMenuGlyph.Exit);
        menu.Items.AddRange([git, desktops, BuildStickyNotesMenu(), new Forms.ToolStripSeparator(),
            settings, about, new Forms.ToolStripSeparator(), exit]);

        menu.Opening += (_, _) => RefreshContextMenu();
        RefreshContextMenu();

        return menu;
    }

    private Forms.ToolStripMenuItem BuildStickyNotesMenu()
    {
        var menu = new Forms.ToolStripMenuItem("Sticky Notes") { Image = MenuIcon(TrayMenuGlyph.Notes) };
        menu.DropDownItems.Add(CreateItem("New note", (_, _) => RunAfterContextMenuClosed(_stickyNotes.CreateNote)));
        var reopen = new Forms.ToolStripMenuItem("Open on this desktop");
        menu.DropDownItems.Add(reopen);
        menu.DropDownItems.Add(new Forms.ToolStripSeparator());
        menu.DropDownItems.Add(CreateItem("Preferences...", (_, _) => OpenSettings(SettingsSection.StickyNotes)));
        menu.DropDownOpening += (_, _) =>
        {
            while (reopen.DropDownItems.Count > 0)
            {
                var item = reopen.DropDownItems[0];
                reopen.DropDownItems.RemoveAt(0);
                item.Dispose();
            }

            foreach (var note in _stickyNotes.Notes)
            {
                var label = $"{note.CustomTitle ?? note.Preview} · {_stickyNotes.DesktopName(note.DesktopId)}";
                if (!note.IsOpen)
                {
                    label += " (closed)";
                }

                reopen.DropDownItems.Add(CreateItem(label.Replace("&", "&&"),
                    (_, _) => RunAfterContextMenuClosed(() => _stickyNotes.OpenOnCurrentDesktop(note))));
            }

            reopen.Enabled = reopen.DropDownItems.Count > 0;
        };
        return menu;
    }

    private void OnStickyNotesError(string message) => ShowBalloon("Sticky notes", message);

    private void RefreshContextMenu()
    {
        if (_modeItem is null || _repositoryItem is null || _activeRepositoryItem is null
            || _branchItem is null || _lastActivityItem is null || _statusItem is null || _rescanWorkspaceItem is null
            || _refreshItem is null || _overlayToggleItem is null || _pinnedRepoModeItem is null
            || _workspaceRepoModeItem is null || _desktopOverlayToggleItem is null)
        {
            return;
        }

        var status = _repositoryWatcher.CurrentStatus;
        var isWorkspaceRepoMode = _settings.WatchMode == RepositoryWatchMode.WorkspaceRepo;

        _modeItem.Text = $"Mode: {FormatWatchModeLabel(_settings.WatchMode)}";
        _repositoryItem.Text = isWorkspaceRepoMode
            ? $"Workspace: {FormatRepositoryLabel(_settings.WorkspaceRootPath)}"
            : $"Pinned repo: {FormatRepositoryLabel(status.RepositoryRoot)}";
        _activeRepositoryItem.Text = $"Repository: {CompactRepositoryLabel(status.RepositoryRoot)}";
        _activeRepositoryItem.ToolTipText = status.RepositoryRoot ?? "";
        _activeRepositoryItem.Visible = true;
        var branch = status.BranchDisplay;
        _branchItem.Text = $"Branch: {(branch.Length > 45 ? branch[..42] + "..." : branch)}";
        _branchItem.ToolTipText = branch;
        _lastActivityItem.Text = $"Last activity: {FormatWorkspaceActivityReason(_sessionController.LastWorkspaceActivityReason)}";
        _lastActivityItem.Visible = isWorkspaceRepoMode && _sessionController.LastWorkspaceActivityReason.HasValue;

        if (!string.IsNullOrWhiteSpace(status.ErrorMessage))
        {
            _statusItem.Text = $"Status: {CompactStatus(status.ErrorMessage)}";
            _statusItem.ToolTipText = status.ErrorMessage;
            _statusItem.Visible = true;
        }
        else if (!string.IsNullOrWhiteSpace(_sessionController.WorkspaceStatusText))
        {
            _statusItem.Text = $"Status: {CompactStatus(_sessionController.WorkspaceStatusText)}";
            _statusItem.ToolTipText = _sessionController.WorkspaceStatusText;
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
        _desktopOverlayToggleItem.Text = _settings.VirtualDesktopOverlayVisible ? "Hide overlay" : "Show overlay";
    }

    private Image MenuIcon(TrayMenuGlyph glyph)
    {
        var image = TrayMenuIcons.Create(glyph);
        _menuImages.Add(image);
        return image;
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

    private void OpenSettings(SettingsSection section)
    {
        RunAfterContextMenuClosed(() =>
        {
            if (_disposed)
            {
                return;
            }

            if (_settingsWindow is null)
            {
                _settingsWindow = new SettingsWindow(_settingsService, _settings, _stickyNotes,
                    _virtualDesktopOverlayController, ApplyOverlayState, ChooseRepository, ChooseWorkspace,
                    SetPinnedRepoMode, SetWorkspaceRepoMode, ToggleStartWithWindows);
                _settingsWindow.Closed += (_, _) => _settingsWindow = null;
                _settingsWindow.Navigate(section);
                _settingsWindow.Show();
            }
            else
            {
                _settingsWindow.Navigate(section);
                if (_settingsWindow.WindowState == WindowState.Minimized)
                {
                    _settingsWindow.WindowState = WindowState.Normal;
                }
            }

            _settingsWindow.Activate();
        });
    }

    private void ToggleDesktopOverlay()
    {
        try
        {
            _virtualDesktopOverlayController.ToggleVisibility();
            RefreshContextMenu();
            _settingsWindow?.RefreshExternalSettings();
        }
        catch (Exception ex)
        {
            ShowError($"Unable to change desktop overlay visibility: {ex.Message}");
        }
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
        _stickyNotes.Dispose();
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
        _settingsWindow?.RefreshRepositoryState();
    }

    private void UpdateNotifyIcon(RepositoryStatus status)
    {
        var text = string.IsNullOrWhiteSpace(status.RepositoryRoot)
            ? "BranchWatch"
            : $"BranchWatch - {status.BranchDisplay}";

        _notifyIcon.Text = text.Length <= 63 ? text : text[..60] + "...";
    }

    private static string CompactStatus(string value) => value.Length > 48 ? value[..45] + "..." : value;

    private static string CompactRepositoryLabel(string? root)
    {
        if (string.IsNullOrWhiteSpace(root)) return "(none)";
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(root));
        return string.IsNullOrWhiteSpace(name) ? root : name;
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

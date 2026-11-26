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
    private readonly OverlayWindowHost<OverlayWindow> _overlay = new(() => new OverlayWindow());
    private readonly VirtualDesktopOverlayController _virtualDesktopOverlayController;
    private readonly StickyNotesController _stickyNotes;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Icon _trayIcon;
    private readonly Forms.Form _dialogOwner;
    private readonly TrayMenu _menu;
    private readonly SettingsWindowHost _settingsHost;
    private readonly UiRefreshCoordinator _repositoryRefresh;
    private bool _disposed;

    public TrayService(
        SettingsService settingsService,
        AppSettings settings,
        StartupService startupService,
        RepositorySessionController sessionController,
        GitRepositoryWatcher repositoryWatcher,
        VirtualDesktopOverlayController virtualDesktopOverlayController,
        StickyNotesController stickyNotes)
    {
        _settingsService = settingsService;
        _settings = settings;
        _startupService = startupService;
        _sessionController = sessionController;
        _repositoryWatcher = repositoryWatcher;
        _virtualDesktopOverlayController = virtualDesktopOverlayController;
        _stickyNotes = stickyNotes;
        _dialogOwner = CreateDialogOwner();
        _trayIcon = ApplicationIcon.LoadTrayIcon();
        _settingsHost = new(() => new SettingsWindow(_settingsService, _settings, _stickyNotes,
            _virtualDesktopOverlayController, ApplyOverlayState, ChooseRepository, ChooseWorkspace,
            SetPinnedRepoMode, SetWorkspaceRepoMode, ToggleStartWithWindows));
        _menu = new TrayMenu(_trayIcon, ReadMenuState, ReadNoteChoices, Dispatch, ReopenNote);
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _trayIcon,
            Text = "BranchWatch",
            Visible = false,
            ContextMenuStrip = _menu.Menu
        };

        var dispatcher = System.Windows.Application.Current.Dispatcher;
        _repositoryRefresh = new(action => dispatcher.BeginInvoke(action), RenderOverlayState);
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
        _repositoryRefresh.Dispose();
        _overlay.Dispose();
        _stickyNotes.Error -= OnStickyNotesError;
        _settingsHost.Dispose();
        _sessionController.StateChanged -= OnSessionStateChanged;
        _repositoryWatcher.StatusChanged -= OnRepositoryStatusChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip = null;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _trayIcon.Dispose();
        _dialogOwner.Dispose();
    }

    private TrayMenuState ReadMenuState() => TrayMenuState.From(_settings, _repositoryWatcher.CurrentStatus,
        _sessionController.LastWorkspaceActivityReason, _sessionController.WorkspaceStatusText);

    private TrayNoteChoice[] ReadNoteChoices()
    {
        var desktops = _stickyNotes.Desktops;
        return TrayMenuState.Notes(_stickyNotes.Notes,
            id => desktops.FirstOrDefault(d => d.Id == id)?.DisplayName ?? "Unavailable desktop");
    }

    private void ReopenNote(Guid id) => RunAfterContextMenuClosed(() =>
    {
        var note = _stickyNotes.Notes.SingleOrDefault(n => n.Id == id);
        if (note is not null) _stickyNotes.OpenOnCurrentDesktop(note);
    });

    private void Dispatch(TrayCommand command)
    {
        if (_disposed) return;
        switch (command)
        {
            case TrayCommand.ToggleOverlay: ToggleOverlay(); break;
            case TrayCommand.RefreshBranch: RefreshBranch(); break;
            case TrayCommand.RescanWorkspace: RescanWorkspace(); break;
            case TrayCommand.ChooseRepository: RunAfterContextMenuClosed(ChooseRepository); break;
            case TrayCommand.ChooseWorkspace: RunAfterContextMenuClosed(ChooseWorkspace); break;
            case TrayCommand.PinnedMode: SetPinnedRepoMode(); break;
            case TrayCommand.WorkspaceMode: SetWorkspaceRepoMode(); break;
            case TrayCommand.GitSettings: OpenSettings(SettingsSection.GitWatcher); break;
            case TrayCommand.ToggleDesktopOverlay: ToggleDesktopOverlay(); break;
            case TrayCommand.MemorySettings: OpenSettings(SettingsSection.DesktopMemory); break;
            case TrayCommand.DesktopSettings: OpenSettings(SettingsSection.VirtualDesktops); break;
            case TrayCommand.GeneralSettings: OpenSettings(SettingsSection.General); break;
            case TrayCommand.About: OpenSettings(SettingsSection.About); break;
            case TrayCommand.Exit: ExitApplication(); break;
            case TrayCommand.NewNote: RunAfterContextMenuClosed(_stickyNotes.CreateNote); break;
            case TrayCommand.NoteSettings: OpenSettings(SettingsSection.StickyNotes); break;
            default: throw new ArgumentOutOfRangeException(nameof(command));
        }
    }

    private void OnStickyNotesError(string message) => ShowBalloon("Sticky notes", message);

    private void RefreshContextMenu() => _menu.Apply(ReadMenuState());

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

    private void OpenSettings(SettingsSection section) =>
        RunAfterContextMenuClosed(() => _settingsHost.Open(section));

    private void ToggleDesktopOverlay()
    {
        try
        {
            _virtualDesktopOverlayController.ToggleVisibility();
            RefreshContextMenu();
            _settingsHost.RefreshExternalSettings();
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
        if (_disposed) return;
        try { GitOverlayVisibility.Set(_settings, !_settings.OverlayVisible, _settingsService.Save); }
        catch (Exception ex)
        {
            ShowError($"Unable to save overlay setting: {ex.Message}");
            return;
        }
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
        _overlay.Release();
        _stickyNotes.Dispose();
        Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    private void OnRepositoryStatusChanged(object? sender, RepositoryStatus status) => _repositoryRefresh.Request();

    private void OnSessionStateChanged(object? sender, EventArgs e) => _repositoryRefresh.Request();

    private void ApplyOverlayState() => _repositoryRefresh.RefreshNow();

    private void RenderOverlayState()
    {
        if (_disposed) return;
        var status = _repositoryWatcher.CurrentStatus;
        var menuState = TrayMenuState.From(_settings, status,
            _sessionController.LastWorkspaceActivityReason, _sessionController.WorkspaceStatusText);
        _notifyIcon.Text = menuState.NotifyText;

        _overlay.Render(_settings.OverlayVisible && !string.IsNullOrWhiteSpace(status.RepositoryRoot), window =>
        {
            var activityReason = WorkspaceActivityReasonFormatter.FormatForOverlay(
                _settings,
                _sessionController.LastWorkspaceActivityReason);
            window.SetOverlayText(status.BranchDisplay, status.RepositoryRoot, activityReason);
            window.ShowOverlay(_settings);
        });

        _menu.Apply(menuState);
        _settingsHost.RefreshRepositoryState();
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
        if (_disposed) return;
        _notifyIcon.ContextMenuStrip?.Close();
        System.Windows.Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            if (!_disposed) action();
        }));
    }

    private void ShowError(string message)
    {
        Forms.MessageBox.Show(_dialogOwner, message, "BranchWatch", Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Error);
    }

    private void ShowBalloon(string title, string message)
    {
        if (!_disposed && _notifyIcon.Visible)
        {
            _notifyIcon.ShowBalloonTip(5000, title, message, Forms.ToolTipIcon.Warning);
        }
    }
}

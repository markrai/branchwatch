using System.Drawing;
using System.IO;
using System.Windows;
using Forms = System.Windows.Forms;

namespace BranchWatch;

public sealed class TrayService : IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly StartupService _startupService;
    private readonly GitRepositoryWatcher _repositoryWatcher;
    private readonly OverlayWindow _overlayWindow;
    private readonly Forms.NotifyIcon _notifyIcon;
    private PersonalizeWindow? _personalizeWindow;
    private Forms.ToolStripMenuItem? _repositoryItem;
    private Forms.ToolStripMenuItem? _branchItem;
    private Forms.ToolStripMenuItem? _statusItem;
    private Forms.ToolStripMenuItem? _refreshItem;
    private Forms.ToolStripMenuItem? _overlayToggleItem;
    private Forms.ToolStripMenuItem? _startupItem;
    private bool _disposed;

    public TrayService(
        SettingsService settingsService,
        AppSettings settings,
        StartupService startupService,
        GitRepositoryWatcher repositoryWatcher,
        OverlayWindow overlayWindow)
    {
        _settingsService = settingsService;
        _settings = settings;
        _startupService = startupService;
        _repositoryWatcher = repositoryWatcher;
        _overlayWindow = overlayWindow;
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "BranchWatch",
            Visible = false,
            ContextMenuStrip = BuildContextMenu()
        };

        _notifyIcon.DoubleClick += (_, _) => ToggleOverlay();
        _repositoryWatcher.StatusChanged += OnRepositoryStatusChanged;
    }

    public void Start()
    {
        _notifyIcon.Visible = true;
        ApplyOverlayState();
    }

    public void SelectRepository(string path, bool showErrors)
    {
        var result = _repositoryWatcher.SelectRepository(path);
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

        _settings.WatchedRepositoryPath = result.RepositoryRoot;
        _settingsService.Save(_settings);
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
        _repositoryWatcher.StatusChanged -= OnRepositoryStatusChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
    }

    private Forms.ContextMenuStrip BuildContextMenu()
    {
        var menu = new Forms.ContextMenuStrip();

        _repositoryItem = CreateDisabledItem("Repository: (none)");
        _branchItem = CreateDisabledItem("Branch: No repository selected");
        _statusItem = CreateDisabledItem(string.Empty);
        _statusItem.Visible = false;

        menu.Items.Add(_repositoryItem);
        menu.Items.Add(_branchItem);
        menu.Items.Add(_statusItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(CreateItem("Choose repository...", (_, _) => ChooseRepository()));
        menu.Items.Add(CreateItem("Personalize...", (_, _) => OpenPersonalize()));

        _refreshItem = CreateItem("Refresh branch", (_, _) => RefreshBranch());
        menu.Items.Add(_refreshItem);

        _overlayToggleItem = CreateItem("Hide overlay", (_, _) => ToggleOverlay());
        menu.Items.Add(_overlayToggleItem);

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
        if (_repositoryItem is null || _branchItem is null || _statusItem is null
            || _refreshItem is null || _overlayToggleItem is null || _startupItem is null)
        {
            return;
        }

        var status = _repositoryWatcher.CurrentStatus;

        _repositoryItem.Text = $"Repository: {FormatRepositoryLabel(status.RepositoryRoot)}";
        _branchItem.Text = $"Branch: {status.BranchDisplay}";

        if (!string.IsNullOrWhiteSpace(status.ErrorMessage))
        {
            _statusItem.Text = $"Status: {status.ErrorMessage}";
            _statusItem.Visible = true;
        }
        else
        {
            _statusItem.Visible = false;
        }

        _refreshItem.Enabled = !string.IsNullOrWhiteSpace(status.RepositoryRoot);
        _overlayToggleItem.Text = _settings.OverlayVisible ? "Hide overlay" : "Show overlay";
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

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            SelectRepository(dialog.SelectedPath, showErrors: true);
        }
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

    private void ApplyOverlayState()
    {
        var status = _repositoryWatcher.CurrentStatus;
        UpdateNotifyIcon(status);

        if (_settings.OverlayVisible && !string.IsNullOrWhiteSpace(status.RepositoryRoot))
        {
            _overlayWindow.SetOverlayText(status.BranchDisplay, status.RepositoryRoot);
            _overlayWindow.ShowOverlay(_settings);
        }
        else
        {
            _overlayWindow.Hide();
        }
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

    private static void ShowError(string message)
    {
        Forms.MessageBox.Show(message, "BranchWatch", Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Error);
    }

    private void ShowBalloon(string title, string message)
    {
        if (_notifyIcon.Visible)
        {
            _notifyIcon.ShowBalloonTip(5000, title, message, Forms.ToolTipIcon.Warning);
        }
    }
}

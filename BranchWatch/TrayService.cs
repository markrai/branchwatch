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
        _repositoryWatcher.StatusChanged -= OnRepositoryStatusChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
    }

    private Forms.ContextMenuStrip BuildContextMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Opening += (_, _) => PopulateContextMenu(menu);
        return menu;
    }

    private void PopulateContextMenu(Forms.ContextMenuStrip menu)
    {
        menu.Items.Clear();

        var status = _repositoryWatcher.CurrentStatus;
        menu.Items.Add(CreateDisabledItem($"Repository: {FormatRepositoryLabel(status.RepositoryRoot)}"));
        menu.Items.Add(CreateDisabledItem($"Branch: {status.BranchDisplay}"));

        if (!string.IsNullOrWhiteSpace(status.ErrorMessage))
        {
            menu.Items.Add(CreateDisabledItem($"Status: {status.ErrorMessage}"));
        }

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(CreateItem("Choose repository...", (_, _) => ChooseRepository()));

        var refreshItem = CreateItem("Refresh branch", (_, _) => RefreshBranch());
        refreshItem.Enabled = !string.IsNullOrWhiteSpace(status.RepositoryRoot);
        menu.Items.Add(refreshItem);

        menu.Items.Add(CreateItem(_settings.OverlayVisible ? "Hide overlay" : "Show overlay", (_, _) => ToggleOverlay()));

        var startupItem = CreateItem("Start with Windows", (_, _) => ToggleStartWithWindows());
        startupItem.Checked = _settings.StartWithWindows;
        startupItem.CheckOnClick = false;
        menu.Items.Add(startupItem);

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(CreateItem("Exit", (_, _) => ExitApplication()));
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
            _overlayWindow.SetBranchText(status.BranchDisplay);
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

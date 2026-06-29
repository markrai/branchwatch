using System.Windows;

namespace BranchWatch;

public partial class App : System.Windows.Application
{
    private SettingsService? _settingsService;
    private StartupService? _startupService;
    private GitRepositoryWatcher? _repositoryWatcher;
    private OverlayWindow? _overlayWindow;
    private TrayService? _trayService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _settingsService = new SettingsService();
        var settings = _settingsService.Load();
        _startupService = new StartupService();

        if (settings.StartWithWindows)
        {
            TryApplyStartupSetting(_startupService, enabled: true);
        }

        _repositoryWatcher = new GitRepositoryWatcher();
        _overlayWindow = new OverlayWindow();
        _trayService = new TrayService(_settingsService, settings, _startupService, _repositoryWatcher, _overlayWindow);
        _trayService.Start();

        if (e.Args.Length > 0)
        {
            _trayService.SelectRepository(e.Args[0], showErrors: true);
        }
        else if (!string.IsNullOrWhiteSpace(settings.WatchedRepositoryPath))
        {
            _trayService.SelectRepository(settings.WatchedRepositoryPath, showErrors: false);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayService?.Dispose();
        _repositoryWatcher?.Dispose();
        _overlayWindow?.Close();
        base.OnExit(e);
    }

    private static void TryApplyStartupSetting(StartupService startupService, bool enabled)
    {
        try
        {
            startupService.SetEnabled(enabled);
        }
        catch
        {
        }
    }
}

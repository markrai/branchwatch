using System.Windows;

namespace BranchWatch;

public partial class App : System.Windows.Application
{
    private SettingsService? _settingsService;
    private StartupService? _startupService;
    private GitRepositoryWatcher? _repositoryWatcher;
    private RepositorySessionController? _sessionController;
    private OverlayWindow? _overlayWindow;
    private TrayService? _trayService;
    private BranchWatchSingleInstance? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = BranchWatchSingleInstance.TryAcquire();
        if (_singleInstance is null)
        {
            Shutdown(0);
            return;
        }

        base.OnStartup(e);

        _settingsService = new SettingsService();
        var settings = _settingsService.Load();
        _startupService = new StartupService();

        if (settings.StartWithWindows)
        {
            TryApplyStartupSetting(_startupService, enabled: true);
        }

        _repositoryWatcher = new GitRepositoryWatcher();
        _sessionController = new RepositorySessionController(_settingsService, settings, _repositoryWatcher);
        _overlayWindow = new OverlayWindow();
        _trayService = new TrayService(
            _settingsService,
            settings,
            _startupService,
            _sessionController,
            _repositoryWatcher,
            _overlayWindow);
        _trayService.Start();

        if (e.Args.Length > 0)
        {
            _trayService.SelectRepository(e.Args[0], showErrors: true);
        }
        else if (settings.WatchMode == RepositoryWatchMode.WorkspaceRepo)
        {
            _trayService.LoadWorkspace();
        }
        else
        {
            _trayService.LoadPinnedRepository();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayService?.Dispose();
        _sessionController?.Dispose();
        _repositoryWatcher?.Dispose();
        _overlayWindow?.Close();
        _singleInstance?.Dispose();
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

using System.Windows;

namespace BranchWatch;

public partial class App : System.Windows.Application
{
    private readonly BranchWatchSingleInstance _singleInstance;
    private SettingsService? _settingsService;
    private StartupService? _startupService;
    private GitRepositoryWatcher? _repositoryWatcher;
    private RepositorySessionController? _sessionController;
    private VirtualDesktopOverlayController? _virtualDesktopOverlayController;
    private TrayService? _trayService;
    private StickyNotesController? _stickyNotes;
    private BranchWatchIpcServer? _ipcServer;
    private BrowserCompanionServer? _browserCompanion;

    public App()
    {
        _singleInstance = SingleInstanceBootstrap.AcquiredInstance
            ?? throw new InvalidOperationException("BranchWatch single instance was not acquired.");
    }

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
        _sessionController = new RepositorySessionController(_settingsService, settings, _repositoryWatcher);
        _ipcServer = new BranchWatchIpcServer(_sessionController);
        _ipcServer.Start();
        _browserCompanion = new BrowserCompanionServer(BrowserCompanionProtocol.PipeName);
        _browserCompanion.Start();
        _virtualDesktopOverlayController = new VirtualDesktopOverlayController(_settingsService, settings);
        _stickyNotes = new StickyNotesController(_virtualDesktopOverlayController.Monitor,
            System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_settingsService.SettingsPath)!, "sticky-notes.json"), settings);
        _trayService = new TrayService(
            _settingsService,
            settings,
            _startupService,
            _sessionController,
            _repositoryWatcher,
            _virtualDesktopOverlayController,
            _stickyNotes);
        _trayService.Start();
        _stickyNotes.Start();
        _virtualDesktopOverlayController.Start();

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

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        base.OnSessionEnding(e);
        if (!e.Cancel)
        {
            _stickyNotes?.Dispose();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _browserCompanion?.Dispose();
        _ipcServer?.Dispose();
        _stickyNotes?.Dispose();
        _trayService?.Dispose();
        _virtualDesktopOverlayController?.Dispose();
        _sessionController?.Dispose();
        _repositoryWatcher?.Dispose();
        _singleInstance.Dispose();
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

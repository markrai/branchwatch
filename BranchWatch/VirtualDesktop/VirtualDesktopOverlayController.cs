using System.Windows.Threading;

namespace BranchWatch;

public sealed class VirtualDesktopOverlayController : IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly VirtualDesktopMonitor _monitor;
    private readonly OverlayWindowHost<VirtualDesktopOverlayWindow> _overlay = new(() => new VirtualDesktopOverlayWindow());
    private readonly UiRefreshCoordinator _refresh;
    private bool _disposed;

    public VirtualDesktopOverlayController(SettingsService settingsService, AppSettings settings)
    {
        _settingsService = settingsService;
        _settings = settings;
        _monitor = new VirtualDesktopMonitor();
        var dispatcher = Dispatcher.CurrentDispatcher;
        _refresh = new(action => dispatcher.BeginInvoke(action), ApplyOverlayState);
        _monitor.StateChanged += OnDisplayStateChanged;
    }

    internal VirtualDesktopMonitor Monitor => _monitor;

    public void Start()
    {
        if (_disposed) return;
        _refresh.RefreshNow();
        _monitor.Start();
    }

    internal void RefreshSettings() => _refresh.RefreshNow();

    internal VirtualDesktopOverlayWindow? CurrentOverlay => _overlay.Current;

    internal bool ToggleVisibility()
    {
        if (_disposed) return _settings.VirtualDesktopOverlayVisible;
        var previous = _settings.VirtualDesktopOverlayVisible;
        _settings.VirtualDesktopOverlayVisible = !previous;
        try
        {
            _settingsService.Save(_settings);
        }
        catch
        {
            _settings.VirtualDesktopOverlayVisible = previous;
            throw;
        }

        _refresh.RefreshNow();
        return _settings.VirtualDesktopOverlayVisible;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _refresh.Dispose();
        _monitor.StateChanged -= OnDisplayStateChanged;
        _monitor.Dispose();
        _overlay.Dispose();
    }

    private void OnDisplayStateChanged(object? sender, VirtualDesktopDisplayState state)
    {
        if (_settings.VirtualDesktopOverlayVisible) _refresh.Request();
    }

    private void ApplyOverlayState()
    {
        _overlay.Render(_settings.VirtualDesktopOverlayVisible, window =>
        {
            var state = _monitor.CurrentState ?? VirtualDesktopRegistryReader.TryGetDisplayState();
            if (state is not null)
            {
                window.SetDisplayState(state);
            }

            window.ShowOverlay(_settings);
        });
    }
}

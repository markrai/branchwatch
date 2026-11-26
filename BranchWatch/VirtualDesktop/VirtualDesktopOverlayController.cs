using System.Windows;

namespace BranchWatch;

public sealed class VirtualDesktopOverlayController : IDisposable
{
    private readonly SettingsService _settingsServiceForVisibility;
    private readonly AppSettings _settings;
    private readonly VirtualDesktopMonitor _monitor;
    private readonly VirtualDesktopOverlayWindow _overlayWindow;
    private bool _disposed;

    public VirtualDesktopOverlayController(SettingsService settingsService, AppSettings settings)
    {
        _settingsServiceForVisibility = settingsService;
        _settings = settings;
        _monitor = new VirtualDesktopMonitor();
        _overlayWindow = new VirtualDesktopOverlayWindow();
        _monitor.StateChanged += OnDisplayStateChanged;
    }

    internal VirtualDesktopMonitor Monitor => _monitor;

    public void Start()
    {
        ApplyOverlayState();
        _monitor.Start();
    }

    internal void RefreshSettings() => ApplyOverlayState();

    internal bool ToggleVisibility()
    {
        var previous = _settings.VirtualDesktopOverlayVisible;
        _settings.VirtualDesktopOverlayVisible = !previous;
        try
        {
            _settingsServiceForVisibility.Save(_settings);
        }
        catch
        {
            _settings.VirtualDesktopOverlayVisible = previous;
            throw;
        }

        ApplyOverlayState();
        return _settings.VirtualDesktopOverlayVisible;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _monitor.StateChanged -= OnDisplayStateChanged;
        _monitor.Dispose();
        _overlayWindow.Close();
    }

    private void OnDisplayStateChanged(object? sender, VirtualDesktopDisplayState state)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            _overlayWindow.SetDisplayState(state);
            ApplyOverlayState();
        });
    }

    private void ApplyOverlayState()
    {
        if (!_settings.VirtualDesktopOverlayVisible)
        {
            _overlayWindow.Hide();
            return;
        }

        var state = _monitor.CurrentState ?? VirtualDesktopRegistryReader.TryGetDisplayState();
        if (state is not null)
        {
            _overlayWindow.SetDisplayState(state);
        }

        _overlayWindow.ShowOverlay(_settings);
    }
}

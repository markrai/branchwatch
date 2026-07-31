using System.Windows;

namespace BranchWatch;

public sealed class VirtualDesktopOverlayController : IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly VirtualDesktopMonitor _monitor;
    private readonly VirtualDesktopOverlayWindow _overlayWindow;
    private VirtualDesktopsWindow? _configWindow;
    private bool _disposed;

    public VirtualDesktopOverlayController(SettingsService settingsService, AppSettings settings)
    {
        _settingsService = settingsService;
        _settings = settings;
        _monitor = new VirtualDesktopMonitor();
        _overlayWindow = new VirtualDesktopOverlayWindow();
        _monitor.CurrentChanged += OnCurrentDesktopChanged;
    }

    public void Start()
    {
        ApplyOverlayState();
        _monitor.Start();
    }

    public void OpenConfigWindow()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (_configWindow is null)
            {
                _configWindow = new VirtualDesktopsWindow(_settingsService, _settings, ApplyOverlayState);
                _configWindow.Closed += (_, _) => _configWindow = null;
                _configWindow.Show();
            }
            else
            {
                _configWindow.Activate();
                _configWindow.Focus();
            }
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _monitor.CurrentChanged -= OnCurrentDesktopChanged;
        _configWindow?.Close();
        _monitor.Dispose();
        _overlayWindow.Close();
    }

    private void OnCurrentDesktopChanged(object? sender, VirtualDesktopInfo desktop)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            _overlayWindow.SetDesktopName(desktop.DisplayName);
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

        var desktop = _monitor.Current ?? VirtualDesktopRegistryReader.TryGetCurrentDesktop();
        if (desktop is not null)
        {
            _overlayWindow.SetDesktopName(desktop.DisplayName);
        }

        _overlayWindow.ShowOverlay(_settings);
    }
}

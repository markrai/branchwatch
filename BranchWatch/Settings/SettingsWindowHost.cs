using System.Windows;

namespace BranchWatch;

internal sealed class SettingsWindowHost(Func<SettingsWindow> create) : IDisposable
{
    private SettingsWindow? _window;
    private bool _disposed;

    internal void Open(SettingsSection section)
    {
        if (_disposed) return;
        if (_window is null)
        {
            var window = create();
            _window = window;
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_window, window)) _window = null;
            };
            window.Navigate(section);
            window.Show();
        }
        else
        {
            _window.Navigate(section);
            if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        }

        _window?.Activate();
    }

    internal void RefreshRepositoryState() => _window?.RefreshRepositoryState();
    internal void RefreshExternalSettings() => _window?.RefreshExternalSettings();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var window = _window;
        _window = null;
        window?.Close();
    }
}

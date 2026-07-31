using System.Windows.Threading;

namespace BranchWatch;

public sealed class VirtualDesktopMonitor : IDisposable
{
    private readonly DispatcherTimer _timer;
    private VirtualDesktopInfo? _current;
    private bool _disposed;

    public event EventHandler<VirtualDesktopInfo>? CurrentChanged;

    public VirtualDesktopMonitor()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _timer.Tick += OnTimerTick;
    }

    public VirtualDesktopInfo? Current => _current;

    public void Start()
    {
        Poll();
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        Poll();
    }

    private void Poll()
    {
        var desktop = VirtualDesktopRegistryReader.TryGetCurrentDesktop();
        if (desktop is null)
        {
            return;
        }

        if (_current is not null && _current.Id == desktop.Id && _current.DisplayName == desktop.DisplayName)
        {
            return;
        }

        _current = desktop;
        CurrentChanged?.Invoke(this, desktop);
    }
}

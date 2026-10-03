using System.Windows.Threading;

namespace BranchWatch;

public sealed class VirtualDesktopMonitor : IDisposable
{
    private readonly DispatcherTimer _timer;
    private VirtualDesktopDisplayState? _currentState;
    private bool _disposed;

    public event EventHandler<VirtualDesktopDisplayState>? StateChanged;

    public VirtualDesktopMonitor()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _timer.Tick += OnTimerTick;
    }

    public VirtualDesktopDisplayState? CurrentState => _currentState;

    public VirtualDesktopInfo? Current => _currentState?.ActiveDesktop;

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
        var state = VirtualDesktopRegistryReader.TryGetDisplayState();
        if (state is null)
        {
            return;
        }

        if (AreStructurallyEqual(_currentState, state))
        {
            return;
        }

        _currentState = state;
        StateChanged?.Invoke(this, state);
    }

    internal static bool AreStructurallyEqual(VirtualDesktopDisplayState? left, VirtualDesktopDisplayState right)
    {
        if (left is null)
        {
            return false;
        }

        if (left.ActiveDesktop.Id != right.ActiveDesktop.Id)
        {
            return false;
        }

        if (!string.Equals(left.ActiveDesktop.DisplayName, right.ActiveDesktop.DisplayName, StringComparison.Ordinal))
        {
            return false;
        }

        if (left.AllDesktops.Count != right.AllDesktops.Count)
        {
            return false;
        }

        for (var i = 0; i < left.AllDesktops.Count; i++)
        {
            var a = left.AllDesktops[i];
            var b = right.AllDesktops[i];
            if (a.Id != b.Id)
            {
                return false;
            }

            if (!string.Equals(a.DisplayName, b.DisplayName, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}

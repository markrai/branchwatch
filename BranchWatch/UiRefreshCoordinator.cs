namespace BranchWatch;

internal sealed class UiRefreshCoordinator(Action<Action> post, Action render) : IDisposable
{
    private readonly object _gate = new();
    private bool _queued;
    private bool _dirty;
    private bool _disposed;

    internal void Request()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _dirty = true;
            if (_queued) return;
            _queued = true;
            try { post(Drain); }
            catch
            {
                _queued = false;
                throw;
            }
        }
    }

    internal void RefreshNow()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _dirty = false;
        }

        render();
    }

    private void Drain()
    {
        lock (_gate)
        {
            _queued = false;
            if (_disposed || !_dirty) return;
            _dirty = false;
        }

        render();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _dirty = false;
        }
    }
}

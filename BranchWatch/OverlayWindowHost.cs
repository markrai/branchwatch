using System.Windows;

namespace BranchWatch;

internal sealed class OverlayWindowHost<T>(Func<T> create) : IDisposable where T : Window
{
    private T? _window;
    private bool _disposed;

    internal T? Current => _window;

    internal void Render(bool visible, Action<T> render)
    {
        if (_disposed) return;
        if (!visible)
        {
            Release();
            return;
        }

        if (_window is { } existing)
        {
            render(existing);
            return;
        }

        var window = create();
        _window = window;
        window.Closed += OnClosed;
        try
        {
            render(window);
        }
        catch
        {
            if (ReferenceEquals(_window, window)) Release();
            throw;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (sender is not T window) return;
        window.Closed -= OnClosed;
        if (ReferenceEquals(_window, window)) _window = null;
    }

    internal void Release()
    {
        if (_window is not { } window) return;
        window.Close();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Release();
    }
}

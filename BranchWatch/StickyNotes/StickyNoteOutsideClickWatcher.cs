using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace BranchWatch;

internal sealed class StickyNoteOutsideClickWatcher : IDisposable
{
    private const int WhMouseLl = 14;
    private readonly IntPtr _owner;
    private readonly Dispatcher _dispatcher;
    private readonly Action _outside;
    private readonly HookProc _callback;
    private IntPtr _hook;
    private bool _disposed;
    private bool _queued;

    private StickyNoteOutsideClickWatcher(IntPtr owner, Dispatcher dispatcher, Action outside)
    {
        _owner = owner;
        _dispatcher = dispatcher;
        _outside = outside;
        _callback = OnMouse;
    }

    internal static StickyNoteOutsideClickWatcher? TryStart(IntPtr owner, Dispatcher dispatcher, Action outside)
    {
        var watcher = new StickyNoteOutsideClickWatcher(owner, dispatcher, outside);
        watcher._hook = SetWindowsHookEx(WhMouseLl, watcher._callback, GetModuleHandle(null), 0);
        return watcher._hook == IntPtr.Zero ? null : watcher;
    }

    private IntPtr OnMouse(int code, IntPtr message, IntPtr data)
    {
        try
        {
            if (code >= 0 && !_disposed && !_queued && message.ToInt64() is 0x201 or 0x204 or 0x207 or 0x20B)
            {
                var point = Marshal.PtrToStructure<NativePoint>(data);
                var target = WindowFromPoint(point);
                if (target != _owner && GetAncestor(target, 2) != _owner && GetAncestor(target, 3) != _owner)
                {
                    _queued = true;
                    _dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                    {
                        if (!_disposed) _outside();
                    }));
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning("Sticky note outside click observation failed: {0}", ex.GetType().Name);
        }
        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        internal int X;
        internal int Y;
    }

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint threadId);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);
}

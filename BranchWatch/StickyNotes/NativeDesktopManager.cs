using System.Runtime.InteropServices;

namespace BranchWatch;

internal interface IStickyNoteDesktopManager : IDisposable
{
    Guid? GetDesktop(IntPtr window);
    bool IsCurrent(IntPtr window);
    bool Move(IntPtr window, Guid target);
}

internal sealed class NativeDesktopManager : IStickyNoteDesktopManager
{
    private IVirtualDesktopManager? _manager;

    private IVirtualDesktopManager Manager => _manager ??= (IVirtualDesktopManager)
        Activator.CreateInstance(Type.GetTypeFromCLSID(
            new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A"), throwOnError: true)!)!;

    public Guid? GetDesktop(IntPtr window)
    {
        try
        {
            return Manager.GetWindowDesktopId(window, out var id) >= 0 && id != Guid.Empty ? id : null;
        }
        catch (COMException)
        {
            Reset();
            return null;
        }
    }

    public bool IsCurrent(IntPtr window)
    {
        try
        {
            return Manager.IsWindowOnCurrentVirtualDesktop(window, out var current) >= 0 && current;
        }
        catch (COMException)
        {
            Reset();
            return false;
        }
    }

    public bool Move(IntPtr window, Guid target)
    {
        try
        {
            return Manager.MoveWindowToDesktop(window, ref target) >= 0 && GetDesktop(window) == target;
        }
        catch (COMException)
        {
            Reset();
            return false;
        }
    }

    private void Reset()
    {
        if (_manager is not null)
        {
            Marshal.FinalReleaseComObject(_manager);
            _manager = null;
        }
    }

    public void Dispose() => Reset();

    [ComImport]
    [Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig]
        int IsWindowOnCurrentVirtualDesktop(IntPtr window, [MarshalAs(UnmanagedType.Bool)] out bool current);

        [PreserveSig]
        int GetWindowDesktopId(IntPtr window, out Guid desktopId);

        [PreserveSig]
        int MoveWindowToDesktop(IntPtr window, ref Guid desktopId);
    }
}

using System.Runtime.InteropServices;

namespace BranchWatch;

internal sealed class ProbeDesktopReader : IDisposable
{
    private IDesktopManager? _manager;

    internal ProbeDesktopMembership Inspect(IntPtr window)
    {
        Guid? desktop = null;
        bool? current = null;
        int desktopResult;
        int currentResult;

        try
        {
            var manager = GetManager();
            desktopResult = manager.GetWindowDesktopId(window, out var id);
            if (desktopResult >= 0 && id != Guid.Empty) desktop = id;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            desktopResult = ex.HResult;
            Dispose();
        }

        try
        {
            var manager = GetManager();
            currentResult = manager.IsWindowOnCurrentVirtualDesktop(window, out var active);
            if (currentResult >= 0) current = active;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            currentResult = ex.HResult;
            Dispose();
        }

        return new ProbeDesktopMembership(desktop, desktopResult, current, currentResult);
    }

    private IDesktopManager GetManager() => _manager ??= (IDesktopManager)
        Activator.CreateInstance(Type.GetTypeFromCLSID(
            new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A"), throwOnError: true)!)!;

    public void Dispose()
    {
        if (_manager is null) return;
        Marshal.FinalReleaseComObject(_manager);
        _manager = null;
    }

    [ComImport]
    [Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopManager
    {
        [PreserveSig]
        int IsWindowOnCurrentVirtualDesktop(IntPtr window, [MarshalAs(UnmanagedType.Bool)] out bool current);

        [PreserveSig]
        int GetWindowDesktopId(IntPtr window, out Guid desktopId);
    }
}

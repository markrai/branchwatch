using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using static BranchWatch.ProbeNative;

namespace BranchWatch;

internal static class DesktopMemoryProbe
{
    internal static DesktopMemoryProbeReport Capture(bool includeIdentities)
    {
        var start = DateTimeOffset.UtcNow;
        var warnings = new List<string>();
        var previousDpi = SetThreadDpiAwarenessContext(new IntPtr(-4));
        if (previousDpi == IntPtr.Zero)
            warnings.Add($"Per-monitor DPI context unavailable (Win32 {Marshal.GetLastPInvokeError()}); screen bounds may be virtualized.");

        try
        {
            var before = ReadDesktops(includeIdentities);
            var monitors = ReadMonitors(includeIdentities, warnings);
            var windows = ReadWindows(includeIdentities, monitors, warnings);
            var afterMonitors = ReadMonitors(includeIdentities, warnings);
            var after = ReadDesktops(includeIdentities);
            var stableTopology = monitors.Count > 0 && ProbePolicy.SameTopology(monitors, afterMonitors);
            var stableDesktop = before is not null && after is not null
                && before.ActiveDesktopHint == after.ActiveDesktopHint
                && before.Desktops.SequenceEqual(after.Desktops);
            if (!stableTopology) warnings.Add("Monitor topology was unavailable or changed during capture.");
            if (!stableDesktop) warnings.Add("Desktop registry hints were unavailable or changed during capture.");
            if (windows.Any(w => w.Classification == "desktop-unverified"))
                warnings.Add("One or more candidate windows have unverified desktop membership; inspect native HRESULTs.");
            if (monitors.Any(m => m.Devices.Count(d => (d.Flags & 1) != 0 && !string.IsNullOrWhiteSpace(d.InterfacePath)) != 1))
                warnings.Add("One or more sources lack exactly one active monitor interface identity; do not assume a unique physical monitor.");
            return new DesktopMemoryProbeReport(1, start, DateTimeOffset.UtcNow,
                Environment.OSVersion.VersionString, RuntimeInformation.ProcessArchitecture.ToString(),
                includeIdentities, previousDpi != IntPtr.Zero, before, after, monitors, afterMonitors,
                stableTopology, stableDesktop, windows, warnings);
        }
        finally
        {
            if (previousDpi != IntPtr.Zero) SetThreadDpiAwarenessContext(previousDpi);
        }
    }

    private static ProbeDesktopState? ReadDesktops(bool includeIdentities)
    {
        var state = VirtualDesktopRegistryReader.TryGetDisplayState();
        return state is null ? null : new ProbeDesktopState(state.ActiveDesktop.Id,
            state.AllDesktops.Select(d => new ProbeDesktop(d.Id, includeIdentities ? d.DisplayName : null)).ToArray());
    }

    private static List<ProbeMonitor> ReadMonitors(bool includeIdentities, List<string> warnings)
    {
        var handles = new List<IntPtr>();
        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
            { handles.Add(monitor); return true; }, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "EnumDisplayMonitors failed.");

        var result = new List<ProbeMonitor>();
        foreach (var handle in handles)
        {
            var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(handle, ref info))
            {
                warnings.Add($"GetMonitorInfo failed (Win32 {Marshal.GetLastPInvokeError()}).");
                continue;
            }

            var devices = new List<ProbeDisplayDevice>();
            for (uint index = 0; index < 64; index++)
            {
                var device = new DisplayDevice { Size = (uint)Marshal.SizeOf<DisplayDevice>() };
                if (!EnumDisplayDevices(info.Device, index, ref device, 1)) break;
                devices.Add(new ProbeDisplayDevice(string.IsNullOrWhiteSpace(device.Id) ? null : device.Id,
                    device.Flags, includeIdentities ? device.Description : null));
            }

            var mode = new DisplayMode { Size = (ushort)Marshal.SizeOf<DisplayMode>() };
            var modeOk = EnumDisplaySettingsEx(info.Device, -1, ref mode, 0);
            if (!modeOk) warnings.Add($"Display mode unavailable for {info.Device}.");
            var scaleResult = GetScaleFactorForMonitor(handle, out var scale);
            result.Add(new ProbeMonitor(info.Device, info.Monitor.ToModel(), info.Work.ToModel(),
                (info.Flags & 1) != 0, modeOk && (mode.Fields & 0x80) != 0 ? ProbePolicy.Rotation(mode.Orientation) : null,
                modeOk ? (int)mode.Width : null, modeOk ? (int)mode.Height : null,
                modeOk ? (int)mode.Frequency : null, scaleResult >= 0 ? scale : null, scaleResult, devices));
        }

        return result;
    }

    private static List<ProbeWindow> ReadWindows(bool includeIdentities, IReadOnlyList<ProbeMonitor> monitors,
        List<string> warnings)
    {
        var handles = new List<IntPtr>();
        if (!EnumWindows((window, _) => { handles.Add(window); return true; }, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "EnumWindows failed.");

        using var desktopReader = new ProbeDesktopReader();
        var result = new List<ProbeWindow>();
        var shell = GetShellWindow();
        var identities = new Dictionary<uint, (string? Name, string? Path)>();
        for (var index = 0; index < handles.Count; index++)
        {
            var window = handles[index];
            if (GetWindowThreadProcessId(window, out var pid) == 0 || pid == 0)
            {
                warnings.Add("A window disappeared during enumeration.");
                continue;
            }

            var visible = IsWindowVisible(window);
            if (!visible) continue;
            if (!identities.TryGetValue(pid, out var identity))
            {
                identity = ReadIdentity(pid);
                identities[pid] = identity;
            }

            var classBuffer = new StringBuilder(256);
            var className = GetClassName(window, classBuffer, classBuffer.Capacity) > 0 ? classBuffer.ToString() : null;
            var own = pid == (uint)Environment.ProcessId || string.Equals(identity.Name, "BranchWatch", StringComparison.OrdinalIgnoreCase);
            var isShell = window == shell || className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Progman" or "WorkerW";
            var style = GetWindowLong(window, -20);
            var owned = GetWindow(window, 4) != IntPtr.Zero;
            var tool = (style & 0x80) != 0;
            var app = (style & 0x40000) != 0;
            var membership = desktopReader.Inspect(window);
            var cloakResult = GetDwmUInt(window, 14, out var cloak, sizeof(uint));
            var frameResult = GetDwmRect(window, 9, out var frame, (uint)Marshal.SizeOf<Rect>());
            var boundsOk = GetWindowRect(window, out var bounds);
            int? boundsError = boundsOk ? null : Marshal.GetLastPInvokeError();
            var placement = new WindowPlacement { Length = (uint)Marshal.SizeOf<WindowPlacement>() };
            var placementOk = GetWindowPlacement(window, ref placement);
            int? placementError = placementOk ? null : Marshal.GetLastPInvokeError();
            var monitor = MonitorFromWindow(window, 2);
            var monitorInfo = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
            var monitorName = monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref monitorInfo)
                && monitors.Any(m => m.SourceName == monitorInfo.Device) ? monitorInfo.Device : null;
            var dpi = GetDpiForWindow(window);
            string? title = null;
            if (includeIdentities && !own && !isShell)
            {
                var titleBuffer = new StringBuilder(513);
                if (GetWindowText(window, titleBuffer, titleBuffer.Capacity) > 0) title = titleBuffer.ToString();
            }

            uint? cloakFlags = cloakResult >= 0 ? cloak : null;
            var classification = ProbePolicy.Classify(visible, owned, tool, app, isShell, own, membership, cloakFlags);
            if (GetWindowThreadProcessId(window, out var currentPid) == 0 || currentPid != pid)
            {
                warnings.Add("A window changed identity during enumeration.");
                continue;
            }

            result.Add(new ProbeWindow($"0x{window.ToInt64():X}", pid, identity.Name,
                includeIdentities && !own && !isShell ? identity.Path : null, title, className,
                visible, owned, tool, app, IsIconic(window), IsZoomed(window), cloakFlags, cloakResult,
                boundsOk ? bounds.ToModel() : null, boundsError,
                frameResult >= 0 ? frame.ToModel() : null, frameResult,
                placementOk ? placement.NormalPosition.ToModel() : null, placementError,
                placementOk ? placement.ShowCommand : null, placementOk ? placement.Flags : null,
                monitorName, dpi == 0 ? null : dpi, membership, classification, index));
        }

        return result;
    }

    private static (string? Name, string? Path) ReadIdentity(uint pid)
    {
        var handle = OpenProcess(0x1000, false, pid);
        if (handle == IntPtr.Zero) return (null, null);
        try
        {
            var buffer = new StringBuilder(32768);
            var size = (uint)buffer.Capacity;
            if (!QueryFullProcessImageName(handle, 0, buffer, ref size)) return (null, null);
            var path = buffer.ToString();
            return (Path.GetFileNameWithoutExtension(path), path);
        }
        finally
        {
            CloseHandle(handle);
        }
    }
}

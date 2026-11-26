using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using static BranchWatch.ProbeNative;

namespace BranchWatch;

internal sealed class NativeExistingWindowRestoreEnvironment : IExistingWindowRestoreEnvironment
{
    public ExistingRestoreInventory Inspect()
    {
        var report = DesktopMemoryProbe.Capture(includeIdentities: true, strictDesktop: true);
        if (!report.PerMonitorDpiContext || !report.TopologyStable || !report.DesktopHintStable
            || report.DesktopsAfter is not { } desktop)
            throw new InvalidOperationException("Could not verify a stable active desktop and display configuration.");
        var topology = new CurrentDisplayTopology(report.FinishedUtc, report.MonitorsAfter.Select(m =>
            new SnapshotMonitor(m.SourceName, m.Bounds, m.WorkArea, m.Primary, m.RotationDegrees,
                m.ModeWidth, m.ModeHeight, m.RefreshHz, m.ScalePercent, m.Devices.ToArray())).ToArray(), report.Warnings);
        var windows = report.Windows.Select(w => new RestoreLiveWindow(w, ReadToken(w))).ToArray();
        return new ExistingRestoreInventory(desktop.ActiveDesktopHint, topology, windows);
    }

    private static RestoreWindowToken? ReadToken(ProbeWindow window)
    {
        if (window.Classification != "candidate" || !window.Handle.StartsWith("0x", StringComparison.Ordinal)
            || !long.TryParse(window.Handle.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var address))
            return null;
        var handle = new IntPtr(address);
        var token = ReadToken(handle);
        return token?.ProcessId == window.ProcessId ? token : null;
    }

    private static RestoreWindowToken? ReadToken(IntPtr window)
    {
        var thread = GetWindowThreadProcessId(window, out var pid);
        if (thread == 0 || pid == 0) return null;
        var process = OpenProcess(0x1000, false, pid);
        if (process == IntPtr.Zero) return null;
        try
        {
            if (!GetProcessTimes(process, out var creation, out _, out _, out _) || creation <= 0)
                return null;
            if (GetWindowThreadProcessId(window, out var currentPid) != thread || currentPid != pid) return null;
            return new(window, pid, thread, creation);
        }
        finally { CloseHandle(process); }
    }

    public ExistingRestoreOutcome Apply(Guid desktopId, CurrentDisplayTopology expectedTopology,
        ExistingRestoreItem item, CancellationToken cancellation)
    {
        ExistingRestoreOutcome Outcome(ExistingRestoreStatus status, string detail) =>
            new(item.WindowId, item.Label, status, detail);
        if (item.Token is not { } token || item.Key is not { } key || item.Placement.ScreenBounds is not { } bounds
            || item.Placement.State != PlannedWindowState.Normal || !DesktopSnapshotCapture.Valid(bounds)
            || bounds.Width > int.MaxValue || bounds.Height > int.MaxValue)
            return Outcome(ExistingRestoreStatus.UnverifiedWindow, "No verified normal window and placement.");
        cancellation.ThrowIfCancellationRequested();
        var previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
        if (previous == IntPtr.Zero)
            return Outcome(ExistingRestoreStatus.Failed, $"Per-monitor DPI context unavailable (Win32 {Marshal.GetLastPInvokeError()}).");
        try
        {
            if (!ExistingWindowRestoreService.SameDisplays(expectedTopology, DesktopMemoryProbe.CaptureDisplays()))
                return Outcome(ExistingRestoreStatus.Changed, "Display configuration changed before placement.");
            using var desktopReader = new ProbeDesktopReader();
            if (!StillEligible(desktopId, token, key, desktopReader))
                return Outcome(ExistingRestoreStatus.Changed, "Window identity, normal state or desktop membership changed before placement.");
            if (GetWindowRect(token.Handle, out var existing) && existing.ToModel() == bounds)
                return Outcome(ExistingRestoreStatus.AlreadyPlaced, "Window is already at the requested bounds.");

            cancellation.ThrowIfCancellationRequested();
            const uint flags = 0x0004 | 0x0010 | 0x0200 | 0x4000;
            if (!SetWindowPos(token.Handle, IntPtr.Zero, bounds.Left, bounds.Top, (int)bounds.Width, (int)bounds.Height, flags))
                return Outcome(ExistingRestoreStatus.Failed, $"Windows rejected placement (Win32 {Marshal.GetLastPInvokeError()}).");

            for (var attempt = 0; attempt < 20; attempt++)
            {
                if (cancellation.IsCancellationRequested)
                    return Outcome(ExistingRestoreStatus.Cancelled, "Placement was requested before cancellation; completion is unverified.");
                if (!StillEligible(desktopId, token, key, desktopReader))
                    return Outcome(ExistingRestoreStatus.Changed, "Placement requested; window or desktop changed before verification.");
                if (GetWindowRect(token.Handle, out var actual) && actual.ToModel() == bounds)
                {
                    if (!ExistingWindowRestoreService.SameDisplays(expectedTopology, DesktopMemoryProbe.CaptureDisplays()))
                        return Outcome(ExistingRestoreStatus.Changed, "Bounds matched but the display configuration changed during placement.");
                    return Outcome(ExistingRestoreStatus.Restored, "Requested screen bounds verified.");
                }
                Thread.Sleep(50);
            }
            return Outcome(ExistingRestoreStatus.Failed, "Placement was requested but exact bounds were not verified. The app may constrain its size or a queued request may complete later.");
        }
        finally { SetThreadDpiAwarenessContext(previous); }
    }

    private static bool StillEligible(Guid desktopId, RestoreWindowToken token, RestoreWindowKey key, ProbeDesktopReader reader)
    {
        var window = token.Handle;
        if (VirtualDesktopRegistryReader.TryGetVerifiedDisplayState()?.ActiveDesktop.Id != desktopId
            || ReadToken(window) != token || token.ProcessId == (uint)Environment.ProcessId
            || !IsWindowVisible(window) || IsIconic(window) || IsZoomed(window) || GetWindow(window, 4) != IntPtr.Zero
            || window == GetShellWindow()) return false;
        var style = GetWindowLong(window, -20);
        var error = Marshal.GetLastPInvokeError();
        if ((style == 0 && error != 0) || (style & 0x80) != 0) return false;
        if (GetDwmUInt(window, 14, out var cloak, sizeof(uint)) < 0 || cloak != 0) return false;

        var className = new StringBuilder(256);
        var title = new StringBuilder(513);
        if (GetClassName(window, className, className.Capacity) <= 0
            || GetWindowText(window, title, title.Capacity) <= 0
            || className.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Progman" or "WorkerW")
            return false;
        var process = OpenProcess(0x1000, false, token.ProcessId);
        if (process == IntPtr.Zero) return false;
        string path;
        try
        {
            var buffer = new StringBuilder(32768);
            var size = (uint)buffer.Capacity;
            if (!QueryFullProcessImageName(process, 0, buffer, ref size)) return false;
            path = buffer.ToString();
        }
        finally { CloseHandle(process); }
        if (!key.Matches(RestoreWindowKey.Create(path, className.ToString(), title.ToString()))) return false;
        var membership = reader.Inspect(window);
        return membership.DesktopHResult >= 0 && membership.CurrentHResult >= 0
            && membership.DesktopId == desktopId && membership.IsCurrent == true
            && VirtualDesktopRegistryReader.TryGetVerifiedDisplayState()?.ActiveDesktop.Id == desktopId
            && ReadToken(window) == token;
    }
}

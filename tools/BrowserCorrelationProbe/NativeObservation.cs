using System.Runtime.InteropServices;
using System.Text;
using static BranchWatch.ProbeNative;

namespace BranchWatch;

internal sealed record NativeBrowserObservation(string Browser, uint ProcessId, uint ThreadId, long ProcessCreation,
    ProbeDesktopMembership Membership, uint Dpi, ProbeRect Bounds, ProbeRect? Frame);

internal static class NativeObservation
{
    internal static BrowserProbeResponse Inspect(BrowserProbeRequest request, IntPtr parent)
    {
        BrowserProbeResponse Reject(string status) => new(1, request.WindowId, request.Nonce, status);
        var previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
        if (previous == IntPtr.Zero) return Reject("dpi-context-unavailable");
        try
        {
            using var reader = new ProbeDesktopReader();
            var first = Read(parent, reader);
            var second = Read(parent, reader);
            if (first is null || second is null) return Reject("parent-unverified");
            if (first != second) return Reject("parent-changed");
            var geometry = Compare(request.Bounds, first.Bounds, first.Frame, first.Dpi);
            return new(1, request.WindowId, request.Nonce, "observed-not-authorized", first.Browser,
                $"0x{parent.ToInt64():X}", first.ProcessId, first.ThreadId, first.ProcessCreation.ToString(System.Globalization.CultureInfo.InvariantCulture),
                first.Membership, first.Dpi, first.Bounds, first.Frame, geometry);
        }
        finally { SetThreadDpiAwarenessContext(previous); }
    }

    internal static string Compare(ProbeRect browser, ProbeRect screen, ProbeRect? frame, uint dpi) =>
        dpi != 96 ? "scaled-coordinate-space-unqualified"
            : browser == screen ? "exact-window-rect"
            : browser == frame ? "exact-visible-frame"
            : "bounds-differ";

    private static NativeBrowserObservation? Read(IntPtr parent, ProbeDesktopReader reader)
    {
        if (parent == IntPtr.Zero || GetAncestor(parent, 2) != parent || !IsWindowVisible(parent)
            || IsIconic(parent) || IsZoomed(parent) || GetWindow(parent, 4) != IntPtr.Zero
            || GetDwmUInt(parent, 14, out var cloak, sizeof(uint)) < 0 || cloak != 0) return null;
        var cls = new StringBuilder(256);
        if (GetClassName(parent, cls, cls.Capacity) <= 0 || cls.ToString() != "Chrome_WidgetWin_1") return null;
        var thread = GetWindowThreadProcessId(parent, out var pid);
        if (thread == 0 || pid == 0) return null;
        var process = OpenProcess(0x1000, false, pid);
        if (process == IntPtr.Zero) return null;
        try
        {
            var path = new StringBuilder(32768);
            var length = (uint)path.Capacity;
            if (!QueryFullProcessImageName(process, 0, path, ref length)
                || !GetProcessTimes(process, out var creation, out _, out _, out _) || creation <= 0) return null;
            var browser = (Path.GetFileName(path.ToString()) ?? string.Empty).ToLowerInvariant() switch
            {
                "chrome.exe" => "chrome",
                "msedge.exe" => "edge",
                _ => null
            };
            if (browser is null || !GetWindowRect(parent, out var rect)) return null;
            var bounds = rect.ToModel();
            if (bounds.Width <= 0 || bounds.Height <= 0) return null;
            var dpi = GetDpiForWindow(parent);
            var membership = reader.Inspect(parent);
            if (dpi == 0 || membership.DesktopId is null || membership.IsCurrent != true
                || membership.DesktopHResult < 0 || membership.CurrentHResult < 0) return null;
            ProbeRect? frame = GetDwmRect(parent, 9, out var visible, (uint)Marshal.SizeOf<Rect>()) >= 0
                ? visible.ToModel() : null;
            if (GetWindowThreadProcessId(parent, out var again) != thread || again != pid) return null;
            return new(browser, pid, thread, creation, membership, dpi, bounds, frame);
        }
        finally { CloseHandle(process); }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);
}

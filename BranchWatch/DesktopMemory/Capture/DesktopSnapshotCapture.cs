using System.IO;

namespace BranchWatch;

internal static class DesktopSnapshotCapture
{
    internal static DesktopSnapshot Capture(Guid desktopId) =>
        FromReport(desktopId, DesktopMemoryProbe.Capture(includeIdentities: true, strictDesktop: true));

    internal static DesktopSnapshot FromReport(Guid desktopId, DesktopMemoryProbeReport report)
    {
        if (desktopId == Guid.Empty || !report.IncludesIdentities || !report.PerMonitorDpiContext
            || !report.TopologyStable || !report.DesktopHintStable
            || report.DesktopsBefore?.ActiveDesktopHint != desktopId
            || report.DesktopsAfter?.ActiveDesktopHint != desktopId)
            throw new InvalidOperationException("Capture requires the selected desktop to be active and a stable per-monitor DPI display configuration.");

        var desktop = report.DesktopsAfter.Desktops.SingleOrDefault(d => d.Id == desktopId)
            ?? throw new InvalidOperationException("The selected desktop no longer exists.");
        var monitors = report.MonitorsBefore.Select(m => new SnapshotMonitor(m.SourceName, m.Bounds, m.WorkArea,
            m.Primary, m.RotationDegrees, m.ModeWidth, m.ModeHeight, m.RefreshHz, m.ScalePercent, m.Devices.ToArray())).ToArray();
        var windows = new List<SnapshotWindow>();
        var excluded = 0;
        var warnings = report.Warnings.ToList();
        foreach (var window in report.Windows)
        {
            var membership = window.Membership;
            if (window.Classification != "candidate" || membership.DesktopId != desktopId
                || membership.IsCurrent != true || membership.DesktopHResult < 0 || membership.CurrentHResult < 0)
            {
                if (window.Classification is not ("branchwatch" or "shell" or "auxiliary")
                    && (membership.DesktopId == desktopId || membership.IsCurrent == true || membership.DesktopId is null))
                    excluded++;
                continue;
            }

            if (window.ScreenBounds is not { } bounds || window.NormalPlacementBounds is not { } normal
                || !Valid(bounds) || !Valid(normal) || window.ShowCommand is not { } show
                || window.MonitorSource is not { } source || !monitors.Any(m => m.SourceName == source))
            {
                excluded++;
                continue;
            }

            var intersecting = monitors.Where(m => Intersects(bounds, m.Bounds)).Select(m => m.SourceName).ToArray();
            windows.Add(new SnapshotWindow(Guid.NewGuid(), window.ProcessName, window.ExecutablePath, window.Title,
                window.ClassName, bounds, window.VisibleFrameBounds, normal, window.Minimized, window.Maximized,
                show, window.PlacementFlags, source, window.WindowDpi, window.EnumerationIndex, intersecting));
        }

        if (excluded > 0) warnings.Add($"{excluded} potentially relevant windows were omitted because identity or placement could not be verified.");
        if (windows.Count == 0) warnings.Add("No eligible application windows were found. An empty capture cannot replace a snapshot.");
        var snapshot = new DesktopSnapshot(desktopId, desktop.Name ?? "Desktop", report.FinishedUtc,
            monitors, windows.ToArray(), excluded, warnings.ToArray());
        DesktopSnapshotValidation.Validate(snapshot);
        return snapshot;
    }

    internal static bool Valid(ProbeRect rect) => rect.Width > 0 && rect.Height > 0;

    internal static bool Intersects(ProbeRect a, ProbeRect b) =>
        Math.Max(a.Left, b.Left) < Math.Min(a.Right, b.Right) && Math.Max(a.Top, b.Top) < Math.Min(a.Bottom, b.Bottom);
}

using System.IO;

namespace BranchWatch;

internal static class DesktopSnapshotValidation
{
    internal static void Validate(DesktopSnapshot snapshot)
    {
        if (snapshot is null || snapshot.DesktopId == Guid.Empty || string.IsNullOrWhiteSpace(snapshot.DesktopName)
            || snapshot.CapturedUtc == default || snapshot.Monitors is null || snapshot.Monitors.Count == 0
            || snapshot.Windows is null || snapshot.Warnings is null || snapshot.ExcludedWindowCount < 0)
            throw new InvalidDataException("Invalid desktop snapshot.");

        var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var monitor in snapshot.Monitors)
        {
            if (monitor is null || string.IsNullOrWhiteSpace(monitor.SourceName) || !sources.Add(monitor.SourceName)
                || !DesktopSnapshotCapture.Valid(monitor.Bounds) || !DesktopSnapshotCapture.Valid(monitor.WorkArea)
                || monitor.Devices is null || monitor.Devices.Any(d => d is null))
                throw new InvalidDataException("Invalid snapshot monitor.");
        }

        if (snapshot.Monitors.Count(m => m.Primary) != 1)
            throw new InvalidDataException("Snapshot must have exactly one primary monitor.");

        var ids = new HashSet<Guid>();
        foreach (var window in snapshot.Windows)
        {
            if (window is null || window.Id == Guid.Empty || !ids.Add(window.Id)
                || !DesktopSnapshotCapture.Valid(window.ScreenBounds) || !DesktopSnapshotCapture.Valid(window.NormalPlacementBounds)
                || string.IsNullOrWhiteSpace(window.MonitorSource) || !sources.Contains(window.MonitorSource)
                || window.IntersectingMonitors is null || window.IntersectingMonitors.Any(m => !sources.Contains(m))
                || window.EnumerationIndex < 0
                || (window.FolderContext is not null && !ApplicationRestoreAdapters.IsValidContext(window, window.FolderContext)))
                throw new InvalidDataException("Invalid snapshot window.");
        }

        if (snapshot.Warnings.Any(w => w is null))
            throw new InvalidDataException("Invalid snapshot warning.");
    }
}

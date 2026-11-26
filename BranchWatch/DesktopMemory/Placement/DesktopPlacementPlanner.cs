using System.IO;

namespace BranchWatch;

internal static class DesktopPlacementPlanner
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    internal static DesktopPlacementPlan Plan(DesktopSnapshot snapshot, CurrentDisplayTopology current,
        UnmatchedMonitorPolicy unmatched = UnmatchedMonitorPolicy.Skip)
    {
        DesktopSnapshotValidation.Validate(snapshot);
        ValidateCurrent(current);
        if (!Enum.IsDefined(unmatched)) throw new ArgumentOutOfRangeException(nameof(unmatched));

        var matches = MonitorMatcher.Match(snapshot.Monitors, current.Monitors);
        var savedBySource = snapshot.Monitors.ToDictionary(m => m.SourceName, Comparer);
        var currentBySource = current.Monitors.ToDictionary(m => m.SourceName, Comparer);
        var bySource = matches.ToDictionary(m => m.SavedSource, Comparer);
        var primary = current.Monitors.Single(m => m.Primary);
        var windows = new List<PlannedWindowPlacement>(snapshot.Windows.Count);
        foreach (var window in snapshot.Windows.OrderBy(w => w.EnumerationIndex))
        {
            var saved = savedBySource[window.MonitorSource];
            var match = bySource[saved.SourceName];
            var state = window.Minimized ? PlannedWindowState.Minimized
                : window.Maximized ? PlannedWindowState.Maximized : PlannedWindowState.Normal;
            var fallback = match.CurrentSource is null;
            if (fallback && unmatched == UnmatchedMonitorPolicy.Skip)
            {
                windows.Add(new(window.Id, null, null, state, WindowPlacementKind.Skipped, true,
                    $"Saved monitor is {MatchDescription(match.Kind)}. Choose a fallback to include this window."));
                continue;
            }

            var target = fallback ? primary : currentBySource[match.CurrentSource!];
            if (state != PlannedWindowState.Normal)
            {
                var raw = window.NormalPlacementBounds;
                var estimate = Translate(raw, (long)saved.WorkArea.Left - saved.Bounds.Left,
                    (long)saved.WorkArea.Top - saved.Bounds.Top);
                if (estimate is null || !DesktopSnapshotCapture.Intersects(estimate.Value, saved.Bounds))
                {
                    windows.Add(new(window.Id, target.SourceName, null, state, WindowPlacementKind.Skipped, true,
                        "Normal placement could not be associated with the saved monitor. Native placement needs review."));
                    continue;
                }

                windows.Add(new(window.Id, target.SourceName, Adapt(estimate.Value, saved.WorkArea, target.WorkArea),
                    state, fallback ? WindowPlacementKind.PrimaryFallback : WindowPlacementKind.Adapted, true,
                    "Estimated normal bounds from raw workspace placement. State and coordinate conversion need native verification before execution."));
                continue;
            }

            var rect = window.ScreenBounds;
            var visible = window.VisibleFrameBounds is { } frame && DesktopSnapshotCapture.Valid(frame)
                && Contains(rect, frame) ? frame : rect;
            var touched = snapshot.Monitors.Where(m => DesktopSnapshotCapture.Intersects(visible, m.Bounds)).ToArray();
            if (!fallback && touched.Length > 1 && TryTranslateSpan(rect, visible, touched, bySource,
                    currentBySource, out var span))
            {
                windows.Add(new(window.Id, target.SourceName, span, state,
                    span == rect ? WindowPlacementKind.Exact : WindowPlacementKind.Translated, false,
                    "Spanning window preserved across matched displays with a common translation."));
                continue;
            }

            if (!fallback && touched.Length <= 1 && SameGeometry(saved, target)
                && Translate(rect, (long)target.Bounds.Left - saved.Bounds.Left,
                    (long)target.Bounds.Top - saved.Bounds.Top) is { } translated
                && Translate(visible, (long)target.Bounds.Left - saved.Bounds.Left,
                    (long)target.Bounds.Top - saved.Bounds.Top) is { } translatedVisible
                && Contains(target.WorkArea, translatedVisible))
            {
                windows.Add(new(window.Id, target.SourceName, translated, state,
                    translated == rect ? WindowPlacementKind.Exact : WindowPlacementKind.Translated, false,
                    translated == rect ? "Original placement retained." : "Placement follows the physical monitor."));
                continue;
            }

            var reason = fallback ? $"Saved monitor is {MatchDescription(match.Kind)}. Explicit primary-monitor fallback."
                : touched.Length > 1 ? "Spanning layout changed; fit the window to its matched anchor monitor."
                : "Fit proportional position and size to the current monitor work area.";
            windows.Add(new(window.Id, target.SourceName, Adapt(rect, saved.WorkArea, target.WorkArea), state,
                fallback ? WindowPlacementKind.PrimaryFallback : WindowPlacementKind.Adapted,
                fallback || touched.Length > 1 || saved.ScalePercent != target.ScalePercent
                    || saved.RotationDegrees != target.RotationDegrees, reason));
        }

        return new(snapshot.DesktopId, snapshot.CapturedUtc, current, matches, windows.ToArray());
    }

    private static string MatchDescription(MonitorMatchKind kind) => kind switch
    {
        MonitorMatchKind.Missing => "disconnected or replaced",
        MonitorMatchKind.Ambiguous => "ambiguous",
        _ => "unverified"
    };

    private static void ValidateCurrent(CurrentDisplayTopology current)
    {
        if (current is null || current.ObservedUtc == default || current.Monitors is null || current.Monitors.Count == 0
            || current.Warnings is null)
            throw new InvalidDataException("Current display configuration is unavailable.");
        var sources = new HashSet<string>(Comparer);
        foreach (var monitor in current.Monitors)
        {
            if (monitor is null || string.IsNullOrWhiteSpace(monitor.SourceName) || !sources.Add(monitor.SourceName)
                || !DesktopSnapshotCapture.Valid(monitor.Bounds) || !DesktopSnapshotCapture.Valid(monitor.WorkArea)
                || !Contains(monitor.Bounds, monitor.WorkArea) || monitor.Devices is null || monitor.Devices.Any(d => d is null))
                throw new InvalidDataException("Invalid current monitor geometry.");
        }
        if (current.Monitors.Count(m => m.Primary) != 1)
            throw new InvalidDataException("Current configuration must have exactly one primary monitor.");
    }

    private static bool SameGeometry(SnapshotMonitor saved, SnapshotMonitor target) =>
        saved.Bounds.Width == target.Bounds.Width && saved.Bounds.Height == target.Bounds.Height
        && saved.RotationDegrees.HasValue && saved.RotationDegrees == target.RotationDegrees
        && saved.ScalePercent.HasValue && saved.ScalePercent == target.ScalePercent
        && Translate(saved.WorkArea, (long)target.Bounds.Left - saved.Bounds.Left,
            (long)target.Bounds.Top - saved.Bounds.Top) == target.WorkArea;

    private static bool TryTranslateSpan(ProbeRect rect, ProbeRect visible, IReadOnlyList<SnapshotMonitor> touched,
        IReadOnlyDictionary<string, MonitorMatch> matches, IReadOnlyDictionary<string, SnapshotMonitor> current,
        out ProbeRect result)
    {
        result = default;
        long? dx = null;
        long? dy = null;
        var areas = new List<ProbeRect>();
        foreach (var saved in touched)
        {
            if (matches[saved.SourceName].CurrentSource is not { } source) return false;
            var target = current[source];
            if (!SameGeometry(saved, target)) return false;
            var x = (long)target.Bounds.Left - saved.Bounds.Left;
            var y = (long)target.Bounds.Top - saved.Bounds.Top;
            if (dx.HasValue && (dx != x || dy != y)) return false;
            dx = x;
            dy = y;
            areas.Add(target.WorkArea);
        }

        if (dx is not { } shiftX || dy is not { } shiftY
            || Translate(rect, shiftX, shiftY) is not { } moved
            || Translate(visible, shiftX, shiftY) is not { } movedVisible
            || !FullyCovered(movedVisible, areas)) return false;
        result = moved;
        return true;
    }

    internal static bool FullyCovered(ProbeRect rect, IReadOnlyList<ProbeRect> areas)
    {
        var clips = areas.Select(a => new ProbeRect(Math.Max(rect.Left, a.Left), Math.Max(rect.Top, a.Top),
            Math.Min(rect.Right, a.Right), Math.Min(rect.Bottom, a.Bottom)))
            .Where(DesktopSnapshotCapture.Valid).ToArray();
        var edges = clips.SelectMany(a => new[] { a.Left, a.Right }).Append(rect.Left).Append(rect.Right)
            .Distinct().OrderBy(x => x).ToArray();
        for (var i = 0; i < edges.Length - 1; i++)
        {
            var end = rect.Top;
            foreach (var clip in clips.Where(a => a.Left <= edges[i] && a.Right >= edges[i + 1]).OrderBy(a => a.Top))
            {
                if (clip.Top > end) return false;
                end = Math.Max(end, clip.Bottom);
                if (end >= rect.Bottom) break;
            }
            if (end < rect.Bottom) return false;
        }
        return DesktopSnapshotCapture.Valid(rect);
    }

    internal static ProbeRect Adapt(ProbeRect window, ProbeRect savedWork, ProbeRect targetWork)
    {
        var width = Math.Clamp(Round(window.Width / (double)savedWork.Width * targetWork.Width), 1, targetWork.Width);
        var height = Math.Clamp(Round(window.Height / (double)savedWork.Height * targetWork.Height), 1, targetWork.Height);
        var x = Math.Clamp((long)targetWork.Left + Round(((long)window.Left - savedWork.Left)
            / (double)savedWork.Width * targetWork.Width), targetWork.Left, (long)targetWork.Right - width);
        var y = Math.Clamp((long)targetWork.Top + Round(((long)window.Top - savedWork.Top)
            / (double)savedWork.Height * targetWork.Height), targetWork.Top, (long)targetWork.Bottom - height);
        return new ProbeRect((int)x, (int)y, (int)(x + width), (int)(y + height));
    }

    private static long Round(double value) => (long)Math.Round(value, MidpointRounding.AwayFromZero);

    private static ProbeRect? Translate(ProbeRect rect, long x, long y)
    {
        var left = rect.Left + x;
        var right = rect.Right + x;
        var top = rect.Top + y;
        var bottom = rect.Bottom + y;
        if (left < int.MinValue || right > int.MaxValue || top < int.MinValue || bottom > int.MaxValue)
            return null;
        return new((int)left, (int)top, (int)right, (int)bottom);
    }

    private static bool Contains(ProbeRect outer, ProbeRect inner) =>
        outer.Left <= inner.Left && outer.Top <= inner.Top && outer.Right >= inner.Right && outer.Bottom >= inner.Bottom;
}

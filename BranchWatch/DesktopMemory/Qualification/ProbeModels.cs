namespace BranchWatch;

internal readonly record struct ProbeRect(int Left, int Top, int Right, int Bottom)
{
    public long Width => (long)Right - Left;
    public long Height => (long)Bottom - Top;
}

internal sealed record ProbeDisplayDevice(string? InterfacePath, uint Flags, string? FriendlyName);

internal sealed record ProbeMonitor(
    string SourceName, ProbeRect Bounds, ProbeRect WorkArea, bool Primary,
    int? RotationDegrees, int? ModeWidth, int? ModeHeight, int? RefreshHz,
    int? ScalePercent, int ScaleHResult, IReadOnlyList<ProbeDisplayDevice> Devices);

internal sealed record ProbeDesktop(Guid Id, string? Name);
internal sealed record ProbeDesktopState(Guid ActiveDesktopHint, IReadOnlyList<ProbeDesktop> Desktops);

internal sealed record ProbeDesktopMembership(Guid? DesktopId, int DesktopHResult, bool? IsCurrent, int CurrentHResult);

internal sealed record ProbeWindow(
    string Handle, uint ProcessId, string? ProcessName, string? ExecutablePath, string? Title,
    string? ClassName, bool Visible, bool Owned, bool ToolWindow, bool AppWindow,
    bool Minimized, bool Maximized, uint? CloakFlags, int CloakHResult, int? StyleError,
    ProbeRect? ScreenBounds, int? BoundsError, ProbeRect? VisibleFrameBounds, int FrameHResult,
    ProbeRect? NormalPlacementBounds, int? PlacementError, uint? ShowCommand, uint? PlacementFlags,
    string? MonitorSource, uint? WindowDpi, ProbeDesktopMembership Membership, string Classification,
    int EnumerationIndex);

internal sealed record DesktopMemoryProbeReport(
    int SchemaVersion, DateTimeOffset StartedUtc, DateTimeOffset FinishedUtc, string WindowsVersion,
    string Architecture, bool IncludesIdentities, bool PerMonitorDpiContext,
    ProbeDesktopState? DesktopsBefore, ProbeDesktopState? DesktopsAfter,
    IReadOnlyList<ProbeMonitor> MonitorsBefore, IReadOnlyList<ProbeMonitor> MonitorsAfter,
    bool TopologyStable, bool DesktopHintStable, IReadOnlyList<ProbeWindow> Windows,
    IReadOnlyList<string> Warnings);

internal static class ProbePolicy
{
    internal static string Classify(bool visible, bool owned, bool toolWindow, bool appWindow,
        bool shell, bool ownApplication, ProbeDesktopMembership membership, uint? cloakFlags, int? styleError = null)
    {
        if (ownApplication) return "branchwatch";
        if (shell) return "shell";
        if (!visible) return "hidden";
        if (styleError.HasValue) return "style-unverified";
        if ((owned || toolWindow) && !appWindow) return "auxiliary";
        if (membership.DesktopId is not { } id || id == Guid.Empty || membership.DesktopHResult < 0
            || membership.CurrentHResult < 0 || membership.IsCurrent is null)
            return "desktop-unverified";
        if (cloakFlags is null) return "cloak-unverified";
        if ((cloakFlags.Value & 5) != 0) return "application-or-inherited-cloak";
        return "candidate";
    }

    internal static int? Rotation(uint value) => value <= 3 ? (int)value * 90 : null;

    internal static bool SameTopology(IReadOnlyList<ProbeMonitor> first, IReadOnlyList<ProbeMonitor> second)
    {
        if (first.Count != second.Count) return false;
        var a = first.OrderBy(m => m.SourceName, StringComparer.OrdinalIgnoreCase).ToArray();
        var b = second.OrderBy(m => m.SourceName, StringComparer.OrdinalIgnoreCase).ToArray();
        return a.Zip(b).All(pair =>
            (pair.First with { Devices = Array.Empty<ProbeDisplayDevice>() })
                == (pair.Second with { Devices = Array.Empty<ProbeDisplayDevice>() })
            && pair.First.Devices.OrderBy(d => d.InterfacePath, StringComparer.OrdinalIgnoreCase)
                .SequenceEqual(pair.Second.Devices.OrderBy(d => d.InterfacePath, StringComparer.OrdinalIgnoreCase)));
    }
}

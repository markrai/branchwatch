using System.Diagnostics;

namespace BranchWatch;

internal static class BrowserAuthorizationPolicy
{
    internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    internal static bool Expired(long started, long now) =>
        now < started || Stopwatch.GetElapsedTime(started, now) >= Lifetime;

    internal static BrowserNativeToken? LiveToken(RestoreLiveWindow live, Guid desktop, string browser)
    {
        var window = live.Window;
        if (desktop == Guid.Empty || !BrowserTabCapture.SupportsLiveSession(browser)
            || window.Membership.DesktopId != desktop || window.Membership.IsCurrent != true
            || window.Membership.DesktopHResult < 0 || window.Membership.CurrentHResult < 0
            || window.Classification != "candidate" || window.CloakFlags != 0 || !window.Visible
            || window.Owned || window.ToolWindow || window.Minimized || window.Maximized
            || BrowserTabCapture.BrowserFor(window.ExecutablePath, window.ClassName) != browser
            || live.Token is not { } token || token.Handle == IntPtr.Zero || token.ProcessId != window.ProcessId
            || token.ProcessId == 0 || token.CreatedFileTime <= 0
            || window.Handle != $"0x{token.Handle.ToInt64():X}") return null;
        return new(window.Handle, token.ProcessId, token.CreatedFileTime.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    internal static bool CanRestoreWindow(SnapshotWindow? window) =>
        window is { Minimized: false, Maximized: false, BrowserContext: { } context }
        && BrowserTabCapture.BrowserFor(window.ExecutablePath, window.ClassName) == context.Browser
        && BrowserRestoreProtocol.CanRestore(context);

    internal static bool MatchesSavedSession(DesktopSnapshot? snapshot, BrowserRestoreKey key, BrowserWindowContext context)
    {
        if (snapshot is null || snapshot.DesktopId != key.DesktopId || snapshot.CapturedUtc != key.CapturedUtc)
            return false;
        var window = snapshot.Windows.SingleOrDefault(w => w.Id == key.WindowId);
        return CanRestoreWindow(window) && window!.BrowserContext is { } saved
            && saved.Browser == context.Browser && saved.ProfileId == context.ProfileId
            && saved.Tabs.SequenceEqual(context.Tabs);
    }
}

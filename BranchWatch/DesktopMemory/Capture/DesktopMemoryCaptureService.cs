namespace BranchWatch;

internal sealed record DesktopCaptureDraft(DesktopSnapshot Snapshot, IReadOnlyDictionary<Guid, BrowserNativeToken> Tokens);

internal sealed class DesktopMemoryCaptureService(
    Func<DesktopMemoryProbeReport> captureReport,
    Func<ExistingRestoreInventory> inspect)
{
    internal static DesktopMemoryCaptureService CreateNative() =>
        new(() => DesktopMemoryProbe.Capture(includeIdentities: true, strictDesktop: true),
            () => new NativeExistingWindowRestoreEnvironment().Inspect());

    internal DesktopCaptureDraft Capture(Guid desktop)
    {
        var report = captureReport();
        var snapshot = DesktopSnapshotCapture.FromReport(desktop, report);
        var tokens = new Dictionary<Guid, BrowserNativeToken>();
        if (!snapshot.Windows.Any(w => BrowserTabCapture.BrowserFor(w.ExecutablePath, w.ClassName) is not null))
            return new(snapshot, tokens);
        try
        {
            var live = inspect();
            if (live.ActiveDesktopId != desktop) return new(snapshot, tokens);
            foreach (var window in snapshot.Windows.Where(w => !w.Minimized && !w.Maximized
                && BrowserTabCapture.BrowserFor(w.ExecutablePath, w.ClassName) is not null))
            {
                var original = report.Windows.SingleOrDefault(w => w.EnumerationIndex == window.EnumerationIndex);
                if (original is null) continue;
                var matches = live.Windows.Where(w => w.Window.Handle == original.Handle
                    && w.Window.ProcessId == original.ProcessId && w.Window.ScreenBounds == window.ScreenBounds
                    && w.Window.Membership.DesktopId == desktop && w.Window.Membership.IsCurrent == true
                    && w.Window.Classification == "candidate" && w.Window.CloakFlags == 0
                    && !w.Window.Owned && !w.Window.ToolWindow && !w.Window.Minimized && !w.Window.Maximized
                    && w.Window.Membership.DesktopHResult >= 0 && w.Window.Membership.CurrentHResult >= 0
                    && string.Equals(w.Window.ExecutablePath, window.ExecutablePath, StringComparison.OrdinalIgnoreCase)
                    && w.Window.ClassName == window.ClassName && w.Window.Title == window.Title && w.Token is not null).ToArray();
                if (matches.Length == 1 && matches[0].Token is { } token)
                    tokens.Add(window.Id, new(original.Handle, token.ProcessId,
                        token.CreatedFileTime.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
        }
        catch (Exception ex)
        {
            tokens.Clear();
            System.Diagnostics.Trace.TraceInformation("Browser capture identities unavailable: {0}", ex.GetType().Name);
        }
        return new(snapshot, tokens);
    }

    internal bool ReverifyBrowser(Guid desktop, BrowserCaptureTicket ticket)
    {
        var inventory = inspect();
        if (inventory.ActiveDesktopId != desktop || ticket.DesktopId != desktop) return false;
        return inventory.Windows.Count(w => w.Token is { } token
            && new BrowserNativeToken(w.Window.Handle, token.ProcessId,
                token.CreatedFileTime.ToString(System.Globalization.CultureInfo.InvariantCulture)) == ticket.Token
            && w.Window.Membership.DesktopId == desktop && w.Window.Membership.IsCurrent == true
            && w.Window.Membership.DesktopHResult >= 0 && w.Window.Membership.CurrentHResult >= 0
            && w.Window.Classification == "candidate" && w.Window.CloakFlags == 0
            && !w.Window.Owned && !w.Window.ToolWindow && !w.Window.Minimized && !w.Window.Maximized
            && BrowserTabCapture.BrowserFor(w.Window.ExecutablePath, w.Window.ClassName) == ticket.Browser) == 1;
    }
}

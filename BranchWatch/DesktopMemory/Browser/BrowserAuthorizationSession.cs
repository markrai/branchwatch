using System.Diagnostics;

namespace BranchWatch;

internal sealed record BrowserImportApproval(DesktopSnapshot Draft, BrowserCaptureTicket Ticket,
    BrowserCaptureEnvelope Envelope, long Revision)
{
    internal BrowserWindowContext Context => new(Envelope.Browser, Envelope.Request.ProfileId, Envelope.Request.Tabs);
}

internal enum BrowserRestoreRequestState { None, Pending, Issued, Expired, Stale }

internal sealed class BrowserAuthorizationSession(
    DesktopMemoryDraft draft, BrowserCaptureInbox inbox, BrowserRestoreQueue queue,
    Func<Guid?> activeDesktop, Func<IReadOnlyList<DesktopSnapshot>> load, Func<long>? clock = null)
{
    private readonly Func<long> _clock = clock ?? Stopwatch.GetTimestamp;
    private long _captureRevision;
    private long _restoreRevision;
    private DesktopSnapshot? _armedDraft;
    private BrowserImportApproval? _import;
    internal BrowserCaptureTicket? CaptureTicket { get; private set; }
    internal BrowserRestoreApproval? RestoreApproval { get; private set; }
    internal long RestoreRevision => _restoreRevision;

    internal void CancelCapture()
    {
        _captureRevision++;
        if (CaptureTicket is { } ticket) inbox.Cancel(ticket.Code);
        CaptureTicket = null;
        _armedDraft = null;
        _import = null;
    }

    internal void CancelRestore()
    {
        _restoreRevision++;
        if (RestoreApproval is { } approval) queue.Cancel(approval.Code);
        RestoreApproval = null;
    }

    internal BrowserCaptureTicket ArmCapture(Guid desktop, Guid windowId)
    {
        if (draft.Snapshot is not { } snapshot || snapshot.DesktopId != desktop || activeDesktop() != desktop
            || !draft.TryGetBrowserToken(windowId, out var token))
            throw new InvalidOperationException("Browser capture no longer belongs to the active draft.");
        var window = snapshot.Windows.SingleOrDefault(w => w.Id == windowId);
        if (window is not { Minimized: false, Maximized: false }
            || BrowserTabCapture.BrowserFor(window.ExecutablePath, window.ClassName) is not { } browser)
            throw new InvalidOperationException("Browser capture is not qualified.");
        CancelCapture();
        var ticket = inbox.Arm(desktop, windowId, browser, token);
        CaptureTicket = ticket;
        _armedDraft = snapshot;
        return ticket;
    }

    internal bool CaptureMatches(DesktopSnapshot? snapshot, Guid? active, Guid? windowId) =>
        CaptureTicket is { } ticket && ReferenceEquals(snapshot, _armedDraft)
        && snapshot?.DesktopId == ticket.DesktopId && active == ticket.DesktopId
        && windowId == ticket.WindowId && !BrowserAuthorizationPolicy.Expired(ticket.Started, _clock());

    internal BrowserImportApproval? TakeCapture(Guid? selected, Guid? windowId)
    {
        if (CaptureTicket is not { } ticket || _armedDraft is not { } snapshot
            || selected != snapshot.DesktopId || !CaptureMatches(draft.Snapshot, activeDesktop(), windowId))
        {
            CancelCapture();
            return null;
        }
        var received = inbox.Take(ticket.Code);
        if (received is null) return null;
        CancelCapture();
        _import = new(snapshot, ticket, received, _captureRevision);
        return _import;
    }

    internal bool ImportCurrent(BrowserImportApproval approval, Guid? selected) =>
        ReferenceEquals(_import, approval) && approval.Revision == _captureRevision
        && ReferenceEquals(draft.Snapshot, approval.Draft) && selected == approval.Draft.DesktopId
        && activeDesktop() == approval.Draft.DesktopId;

    internal bool AttachCapture(BrowserImportApproval approval, Guid? selected)
    {
        if (!ImportCurrent(approval, selected)) return false;
        _import = null;
        return draft.TryAttachBrowser(approval.Draft, approval.Ticket, approval.Context);
    }

    internal bool WasIssued(BrowserRestoreKey key) => queue.WasIssued(key);

    internal BrowserRestoreApproval ArmRestore(DesktopSnapshot snapshot, Guid windowId, long expectedRevision)
    {
        if (expectedRevision != _restoreRevision || draft.Snapshot is not null)
            throw new InvalidOperationException("Browser restore approval changed.");
        var window = snapshot.Windows.SingleOrDefault(w => w.Id == windowId);
        if (!BrowserAuthorizationPolicy.CanRestoreWindow(window))
            throw new InvalidOperationException("Browser session cannot be restored.");
        var key = new BrowserRestoreKey(snapshot.DesktopId, snapshot.CapturedUtc, windowId);
        var source = window!.BrowserContext!;
        var context = source with { Tabs = source.Tabs.ToArray() };
        bool StillCurrent() => activeDesktop() == key.DesktopId
            && BrowserAuthorizationPolicy.MatchesSavedSession(load().SingleOrDefault(s => s.DesktopId == key.DesktopId), key, context);
        if (!StillCurrent()) throw new InvalidOperationException("Saved browser session or active desktop changed.");
        CancelRestore();
        var approval = queue.Arm(key, context, StillCurrent);
        RestoreApproval = approval;
        return approval;
    }

    internal BrowserRestoreRequestState GetRestoreState(DesktopSnapshot? snapshot, Guid? active, bool busy)
    {
        if (RestoreApproval is not { } approval) return BrowserRestoreRequestState.None;
        if (busy || draft.Snapshot is not null || active != approval.Key.DesktopId
            || !BrowserAuthorizationPolicy.MatchesSavedSession(snapshot, approval.Key, approval.Context))
            return BrowserRestoreRequestState.Stale;
        if (queue.WasIssued(approval.Key)) return BrowserRestoreRequestState.Issued;
        return BrowserAuthorizationPolicy.Expired(approval.Started, _clock())
            ? BrowserRestoreRequestState.Expired : BrowserRestoreRequestState.Pending;
    }
}

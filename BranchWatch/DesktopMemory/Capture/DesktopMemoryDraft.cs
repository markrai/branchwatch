namespace BranchWatch;

internal sealed class DesktopMemoryDraft
{
    private Dictionary<Guid, BrowserNativeToken> _tokens = [];
    private readonly BrowserTabReview _review = new();

    internal DesktopSnapshot? Snapshot { get; private set; }

    internal void Replace(DesktopCaptureDraft capture)
    {
        DesktopSnapshotValidation.Validate(capture.Snapshot);
        var tokens = capture.Tokens.ToDictionary(p => p.Key, p => p.Value);
        if (tokens.Keys.Any(id => !capture.Snapshot.Windows.Any(w => w.Id == id && !w.Minimized && !w.Maximized
            && BrowserTabCapture.BrowserFor(w.ExecutablePath, w.ClassName) is not null)))
            throw new InvalidOperationException("Capture contains an invalid browser identity.");

        Clear();
        Snapshot = capture.Snapshot;
        _tokens = tokens;
        _review.Synchronize(Snapshot);
    }

    internal void Clear()
    {
        Snapshot = null;
        _tokens.Clear();
        _review.Clear();
    }

    internal bool HasBrowserToken(Guid windowId) => _tokens.ContainsKey(windowId);

    internal bool TryGetBrowserToken(Guid windowId, out BrowserNativeToken token)
    {
        if (_tokens.TryGetValue(windowId, out var found))
        {
            token = found;
            return true;
        }
        token = null!;
        return false;
    }

    internal bool IsSelected(Guid windowId, int index) => _review.IsSelected(windowId, index);
    internal void SetSelected(Guid windowId, int index, bool selected) => _review.Set(windowId, index, selected);
    internal void SetAll(Guid windowId, bool selected) => _review.SetAll(windowId, selected);

    internal bool TrySetFolder(DesktopSnapshot expected, Guid windowId, ApplicationFolderContext? context)
    {
        if (!ReferenceEquals(Snapshot, expected)) return false;
        var window = expected.Windows.SingleOrDefault(w => w.Id == windowId);
        if (window is null || ApplicationRestoreAdapters.Detect(window) is null
            || context is not null && !ApplicationRestoreAdapters.IsValidContext(window, context)) return false;
        return ReplaceWindow(expected, window with { FolderContext = context });
    }

    internal bool TryAttachBrowser(DesktopSnapshot expected, BrowserCaptureTicket ticket, BrowserWindowContext context)
    {
        if (!ReferenceEquals(Snapshot, expected) || ticket.DesktopId != expected.DesktopId
            || !_tokens.TryGetValue(ticket.WindowId, out var token) || token != ticket.Token
            || ticket.Browser != context.Browser || !BrowserTabCapture.SupportsLiveSession(context.Browser)
            || !BrowserTabCapture.IsValid(context) || context.Tabs.Count == 0) return false;
        var window = expected.Windows.SingleOrDefault(w => w.Id == ticket.WindowId);
        if (window is null || window.Minimized || window.Maximized
            || BrowserTabCapture.BrowserFor(window.ExecutablePath, window.ClassName) != ticket.Browser) return false;
        return ReplaceWindow(expected, window with { BrowserContext = context with { Tabs = context.Tabs.ToArray() } });
    }

    private bool ReplaceWindow(DesktopSnapshot expected, SnapshotWindow window)
    {
        var updated = expected with { Windows = expected.Windows.Select(w => w.Id == window.Id ? window : w).ToArray() };
        DesktopSnapshotValidation.Validate(updated);
        Snapshot = updated;
        _review.Synchronize(updated);
        return true;
    }

    internal void Save(DesktopMemoryStore store, DesktopSnapshot expected, DateTimeOffset? expectedCapture)
    {
        if (!ReferenceEquals(Snapshot, expected)) throw new InvalidOperationException("The unsaved capture changed. Review it before saving.");
        if (expected.Windows.Count == 0) throw new InvalidOperationException("An empty capture cannot be saved.");
        store.Save(_review.Apply(expected), expectedCapture);
        Clear();
    }
}

namespace BranchWatch;

internal sealed class BrowserTabReview
{
    private sealed record Entry(BrowserWindowContext Context, bool[] Selected);
    private readonly Dictionary<Guid, Entry> _entries = [];

    internal void Synchronize(DesktopSnapshot? draft)
    {
        var windows = draft?.Windows.Where(w => w.BrowserContext is not null).ToDictionary(w => w.Id) ?? [];
        foreach (var id in _entries.Keys.Where(id => !windows.ContainsKey(id)).ToArray()) _entries.Remove(id);
        foreach (var (id, window) in windows)
        {
            var context = window.BrowserContext!;
            if (!_entries.TryGetValue(id, out var entry) || !ReferenceEquals(entry.Context, context))
                _entries[id] = new(context, Enumerable.Repeat(true, context.Tabs.Count).ToArray());
        }
    }

    internal void Clear() => _entries.Clear();

    internal bool IsSelected(Guid window, int index) => Get(window).Selected[index];

    internal void Set(Guid window, int index, bool selected) => Get(window).Selected[index] = selected;

    internal void SetAll(Guid window, bool selected) => Array.Fill(Get(window).Selected, selected);

    internal int SelectedCount(Guid window) => Get(window).Selected.Count(selected => selected);

    internal DesktopSnapshot Apply(DesktopSnapshot draft)
    {
        var windows = draft.Windows.Select(window =>
        {
            if (window.BrowserContext is not { } context) return window;
            var entry = Get(window.Id);
            if (!ReferenceEquals(entry.Context, context) || entry.Selected.Length != context.Tabs.Count)
                throw new InvalidOperationException("Browser tabs changed. Review the new capture before saving.");
            return window with
            {
                BrowserContext = context with
                {
                    Tabs = context.Tabs.Where((_, index) => entry.Selected[index]).ToArray()
                }
            };
        }).ToArray();
        var result = draft with { Windows = windows };
        DesktopSnapshotValidation.Validate(result);
        return result;
    }

    private Entry Get(Guid window) => _entries.TryGetValue(window, out var entry)
        ? entry : throw new InvalidOperationException("Browser tab review is unavailable.");
}

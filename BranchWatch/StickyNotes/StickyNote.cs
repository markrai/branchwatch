namespace BranchWatch;

internal sealed class StickyNote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DesktopId { get; set; }
    public string Text { get; set; } = "";
    private string? _customTitle;
    public string? CustomTitle
    {
        get => _customTitle;
        set => _customTitle = NormalizeTitle(value);
    }

    internal const int MaxTitleLength = 120;

    private static string? NormalizeTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var title = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        title = string.Concat(title.Where(c => !char.IsControl(c)));
        if (title.Length > MaxTitleLength) title = title[..MaxTitleLength].TrimEnd();
        return string.IsNullOrWhiteSpace(title) ? null : title;
    }

    internal string DisplayTitle(string desktopName) => CustomTitle ?? desktopName;
    public string Color { get; set; } = "Yellow";
    private int _transparencyLevel = StickyNoteTransparency.DefaultLevel;

    public int TransparencyLevel
    {
        get => _transparencyLevel;
        set => _transparencyLevel = StickyNoteTransparency.ClampLevel(value);
    }

    public bool IsOpen { get; set; } = true;
    public bool IsCollapsed { get; set; }
    public double Left { get; set; }
    public double Top { get; set; }
    internal const double DefaultWidth = 300;
    internal const double DefaultHeight = 260;

    public double Width { get; set; } = DefaultWidth;
    public double Height { get; set; } = DefaultHeight;
    public double? OriginalWidth { get; set; }
    public double? OriginalHeight { get; set; }

    internal void EnsureOriginalSize()
    {
        OriginalWidth = ResolveOriginalDimension(OriginalWidth, Width, DefaultWidth);
        OriginalHeight = ResolveOriginalDimension(OriginalHeight, Height, DefaultHeight);
    }

    private static double ResolveOriginalDimension(double? original, double current, double fallback)
    {
        var known = original is > 0 && double.IsFinite(original.Value) ? original.Value : fallback;
        return double.IsFinite(current) && current > 0 ? Math.Max(known, current) : known;
    }

    internal static IEnumerable<StickyNote> ForDesktop(IEnumerable<StickyNote> notes, Guid desktopId) =>
        notes.Where(note => note.IsOpen && note.DesktopId == desktopId);

    internal string Preview
    {
        get
        {
            var text = string.Join(" ", Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return text.Length switch
            {
                0 => "Untitled note",
                > 40 => text[..39] + "…",
                _ => text
            };
        }
    }
}

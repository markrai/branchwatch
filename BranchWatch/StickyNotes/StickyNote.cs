namespace BranchWatch;

internal sealed class StickyNote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DesktopId { get; set; }
    public string Text { get; set; } = "";
    public string Color { get; set; } = "Yellow";
    public bool IsOpen { get; set; } = true;
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; } = 300;
    public double Height { get; set; } = 260;

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

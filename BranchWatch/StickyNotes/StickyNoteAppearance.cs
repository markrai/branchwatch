namespace BranchWatch;

internal sealed record StickyNoteColor(string Name, string Body, string Header, string Alarm);

internal readonly record struct StickyNoteAppearance(string Color, int TransparencyLevel)
{
    internal static IReadOnlyList<StickyNoteColor> Palette { get; } = Array.AsReadOnly(new StickyNoteColor[]
    {
        new("Yellow", "#FFF5C5", "#F6DE87", "#D6A51E"),
        new("Green", "#DFF5D8", "#B8E2AD", "#55AA47"),
        new("Pink", "#FCE1E8", "#F3BACB", "#DB6391"),
        new("Blue", "#DDEEFF", "#B7D7F4", "#4A9CE5"),
        new("Purple", "#EEE3FC", "#D8C3F1", "#9C70DB"),
        new("White", "#F5F5F5", "#DDDDDD", "#CFCFCF")
    });

    internal static StickyNoteColor ResolveColor(string? name) =>
        Palette.FirstOrDefault(color => string.Equals(color.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? Palette[0];

    internal static StickyNoteAppearance FromSettings(AppSettings settings) =>
        new(ResolveColor(settings.StickyNoteDefaultColor).Name,
            StickyNoteTransparency.ClampLevel(settings.StickyNoteDefaultTransparencyLevel));

    internal void ApplyTo(StickyNote note)
    {
        note.Color = ResolveColor(Color).Name;
        note.TransparencyLevel = TransparencyLevel;
    }

    internal void SaveDefaults(SettingsService service, AppSettings settings)
    {
        var previousColor = settings.StickyNoteDefaultColor;
        var previousLevel = settings.StickyNoteDefaultTransparencyLevel;

        settings.StickyNoteDefaultColor = ResolveColor(Color).Name;
        settings.StickyNoteDefaultTransparencyLevel = StickyNoteTransparency.ClampLevel(TransparencyLevel);
        try
        {
            service.Save(settings);
        }
        catch
        {
            settings.StickyNoteDefaultColor = previousColor;
            settings.StickyNoteDefaultTransparencyLevel = previousLevel;
            throw;
        }
    }
}

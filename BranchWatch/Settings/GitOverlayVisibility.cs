namespace BranchWatch;

internal static class GitOverlayVisibility
{
    internal static void Set(AppSettings settings, bool visible, Action<AppSettings> save)
    {
        var previous = settings.OverlayVisible;
        settings.OverlayVisible = visible;
        try { save(settings); }
        catch
        {
            settings.OverlayVisible = previous;
            throw;
        }
    }
}

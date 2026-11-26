namespace BranchWatch;

internal static class VirtualDesktopColors
{
    internal static System.Windows.Media.Color Resolve(AppSettings settings, Guid? id)
    {
        if (id is { } desktop && settings.VirtualDesktopFontColors?.TryGetValue(desktop, out var hex) == true
            && IsValidHex(hex))
        {
            return OverlaySettings.ParseFontColor(hex);
        }

        return OverlaySettings.ParseFontColor(settings.VirtualDesktopOverlayFontColor);
    }

    private static bool IsValidHex(string? value) =>
        value is { Length: 7 } && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit);
}

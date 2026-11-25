using System.Windows;
using System.Windows.Media;

namespace BranchWatch;

internal readonly record struct StickyNoteResizeBounds(double Left, double Top, double Width, double Height);

internal static class StickyNoteResizeGeometry
{
    internal const double GripSize = 18;

    internal static StickyNoteResizeBounds Resize(StickyNoteResizeBounds start,
        double deltaX, double deltaY, double minWidth, double minHeight, double maxWidth, double maxHeight)
    {
        var width = Math.Clamp(start.Width - deltaX, minWidth, Math.Max(minWidth, maxWidth));
        var height = Math.Clamp(start.Height + deltaY, minHeight, Math.Max(minHeight, maxHeight));
        return new StickyNoteResizeBounds(start.Left + start.Width - width, start.Top, width, height);
    }

    internal static Geometry PassiveBodyClip(double width, double height)
    {
        var gripWidth = Math.Min(GripSize, width);
        var gripHeight = Math.Min(GripSize, height);
        return new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(new Rect(0, 0, width, height)),
            new RectangleGeometry(new Rect(0, height - gripHeight, gripWidth, gripHeight)));
    }
}

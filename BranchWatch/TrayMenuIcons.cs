using System.Drawing;
using System.Drawing.Drawing2D;

namespace BranchWatch;

internal enum TrayMenuGlyph
{
    Git,
    Desktops,
    Notes,
    Settings,
    About,
    Exit
}

internal static class TrayMenuIcons
{
    internal static Bitmap Create(TrayMenuGlyph glyph)
    {
        var image = new Bitmap(16, 16);
        using var graphics = Graphics.FromImage(image);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(System.Drawing.SystemColors.MenuText, 1.2f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        switch (glyph)
        {
            case TrayMenuGlyph.Git:
                graphics.DrawLine(pen, 4, 5, 4, 11);
                graphics.DrawBezier(pen, 11, 5, 11, 9, 4, 7, 4, 11);
                graphics.DrawEllipse(pen, 2, 1, 4, 4);
                graphics.DrawEllipse(pen, 9, 1, 4, 4);
                graphics.DrawEllipse(pen, 2, 11, 4, 4);
                break;
            case TrayMenuGlyph.Desktops:
                graphics.DrawRectangle(pen, 1, 2, 11, 8);
                graphics.DrawLine(pen, 5, 12, 14, 12);
                graphics.DrawLine(pen, 14, 12, 14, 5);
                break;
            case TrayMenuGlyph.Notes:
                graphics.DrawPolygon(pen, new Point[] { new(2, 1), new(14, 1), new(14, 9), new(8, 15), new(2, 15) });
                graphics.DrawLines(pen, new Point[] { new(8, 15), new(8, 9), new(14, 9) });
                break;
            case TrayMenuGlyph.Settings:
                graphics.DrawEllipse(pen, 3, 3, 10, 10);
                graphics.DrawEllipse(pen, 6, 6, 4, 4);
                for (var i = 0; i < 8; i++)
                {
                    var angle = i * Math.PI / 4;
                    graphics.DrawLine(pen, (float)(8 + 5 * Math.Cos(angle)), (float)(8 + 5 * Math.Sin(angle)),
                        (float)(8 + 7 * Math.Cos(angle)), (float)(8 + 7 * Math.Sin(angle)));
                }
                break;
            case TrayMenuGlyph.About:
                graphics.DrawEllipse(pen, 1, 1, 14, 14);
                graphics.DrawLine(pen, 8, 7, 8, 11);
                graphics.DrawLine(pen, 8, 4, 8, 4.3f);
                break;
            case TrayMenuGlyph.Exit:
                graphics.DrawLines(pen, new Point[] { new(8, 2), new(2, 2), new(2, 14), new(8, 14) });
                graphics.DrawLine(pen, 6, 8, 14, 8);
                graphics.DrawLines(pen, new Point[] { new(11, 5), new(14, 8), new(11, 11) });
                break;
        }

        return image;
    }
}

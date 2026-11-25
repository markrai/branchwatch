using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfBorder = System.Windows.Controls.Border;
using WpfPoint = System.Windows.Point;

namespace BranchWatch;

internal sealed class StickyNotePassiveBody
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExLayered = 0x80000;
    private const int WsExNoActivate = 0x08000000;
    private const uint SwpNoActivate = 0x10;
    private const uint SwpNoZOrder = 0x4;

    private readonly StickyNoteWindow _owner;
    private readonly Window _window;
    private readonly WpfBorder _surface;
    private readonly WpfTextBox _text;
    private IntPtr _handle;
    private (int X, int Y, int Width, int Height)? _bounds;
    private string? _color;
    private int _level;

    internal StickyNotePassiveBody(StickyNoteWindow owner, StickyNote note)
    {
        _owner = owner;
        _text = new WpfTextBox
        {
            Margin = new Thickness(7, 4, 7, 7),
            Padding = new Thickness(8, 7, 8, 7),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = BrushFor("#29251F"),
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            IsReadOnly = true,
            Focusable = false,
            IsHitTestVisible = false,
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled
        };
        _surface = new WpfBorder { Child = _text };
        _window = new Window
        {
            Owner = owner,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            Focusable = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Width = 1,
            Height = 1,
            Content = _surface
        };
        UpdateContent(note);
    }

    internal bool IsVisible => _window.IsVisible;

    internal void UpdateContent(StickyNote note)
    {
        if (_text.Text != note.Text)
        {
            _text.Text = note.Text;
        }

        if (_color != note.Color)
        {
            _color = note.Color;
            _surface.Background = BrushFor(StickyNoteAppearance.ResolveColor(note.Color).Body);
        }

        if (_level != note.TransparencyLevel)
        {
            _level = note.TransparencyLevel;
            _surface.Opacity = StickyNoteTransparency.OpacityForLevel(_level);
        }
    }

    internal bool TryShow(FrameworkElement host, StickyNotesController controller)
    {
        var helper = new WindowInteropHelper(_window);
        _handle = helper.EnsureHandle();
        var style = GetWindowLong(_handle, GwlExStyle);
        if ((style & WsExLayered) == 0)
        {
            return false;
        }

        var desired = style | WsExTransparent | WsExNoActivate;
        SetWindowLong(_handle, GwlExStyle, desired);
        if ((GetWindowLong(_handle, GwlExStyle) & desired) != desired
            || !TryAlign(host))
        {
            return false;
        }

        if (!controller.IsPassiveBodyOnOwnerDesktop(_handle, _owner.Handle))
        {
            return false;
        }

        _window.Show();
        return controller.IsPassiveBodyOnOwnerDesktop(_handle, _owner.Handle)
            && (GetWindowLong(_handle, GwlExStyle) & WsExTransparent) != 0
            && TryAlign(host);
    }

    internal bool TryAlign(FrameworkElement host)
    {
        if (_handle == IntPtr.Zero || host.ActualWidth <= 0 || host.ActualHeight <= 0)
        {
            return false;
        }

        var topLeft = host.PointToScreen(new WpfPoint(0, 0));
        var bottomRight = host.PointToScreen(new WpfPoint(host.ActualWidth, host.ActualHeight));
        var x = (int)Math.Round(topLeft.X);
        var y = (int)Math.Round(topLeft.Y);
        var width = Math.Max(1, (int)Math.Round(bottomRight.X) - x);
        var height = Math.Max(1, (int)Math.Round(bottomRight.Y) - y);
        var bounds = (x, y, width, height);
        if (_bounds == bounds)
        {
            return true;
        }

        if (!SetWindowPos(_handle, IntPtr.Zero, x, y, width, height, SwpNoActivate | SwpNoZOrder))
        {
            return false;
        }

        _bounds = bounds;
        return true;
    }

    internal void Close() => _window.Close();

    private static SolidColorBrush BrushFor(string value) =>
        new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value)!);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr handle, int index, int value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}

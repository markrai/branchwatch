using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfBorder = System.Windows.Controls.Border;
using WpfPoint = System.Windows.Point;

namespace BranchWatch;

internal sealed class StickyNotePassiveBody : IStickyNotePassiveBody
{
    private const int GwlExStyle = -20;
    private const uint GwOwner = 4;
    private const int WsExTransparent = 0x20;
    private const int WsExLayered = 0x80000;
    private const int WsExNoActivate = 0x08000000;
    private const uint SwpNoActivate = 0x10;
    private const uint SwpNoZOrder = 0x4;

    private readonly StickyNoteWindow _owner;
    private readonly FrameworkElement _host;
    private readonly StickyNotesController _controller;
    private bool _closed;
    private readonly Window _window;
    private readonly WpfBorder _surface;
    private readonly WpfTextBox _text;
    private readonly System.Windows.Controls.Grid _root;
    private readonly StickyNoteAlarmPulse _alarmPulse;
    private IntPtr _handle;
    private (int X, int Y, int Width, int Height)? _bounds;
    private string? _color;
    private int _level;

    internal StickyNotePassiveBody(StickyNoteWindow owner, FrameworkElement host, StickyNotesController controller, StickyNote note)
    {
        _owner = owner;
        _host = host;
        _controller = controller;
        _text = new WpfTextBox
        {
            Margin = new Thickness(7, 4, 7, 7),
            Padding = new Thickness(8, 7, 8, 7),
            Background = System.Windows.Media.Brushes.Transparent,
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
        _text.Resources = new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/BranchWatch;component/StickyNotes/StickyNoteScrollBar.xaml")
        };
        _surface = new WpfBorder { Child = _text };
        _root = new System.Windows.Controls.Grid { ClipToBounds = true };
        _root.Children.Add(_surface);
        var outline = new WpfBorder { BorderThickness = new Thickness(3, 0, 3, 3), CornerRadius = new CornerRadius(0, 0, 3, 3),
            IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        _root.Children.Add(outline);
        _alarmPulse = new StickyNoteAlarmPulse(outline);
        _root.SizeChanged += (_, e) => _root.Clip = StickyNoteResizeGeometry.PassiveBodyClip(e.NewSize.Width, e.NewSize.Height);
        _window = new Window
        {
            Owner = owner,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            Focusable = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            UseLayoutRounding = true,
            Width = 1,
            Height = 1,
            Content = _root
        };
        _window.Closed += (_, _) =>
        {
            _closed = true;
            _handle = IntPtr.Zero;
            _bounds = null;
            _alarmPulse.Update(false, "");
        };
        UpdateContent(note);
    }

    public bool IsVisible => !_closed && _window.IsVisible;
    internal IntPtr Handle => _handle;
    internal bool HasNativeOwner => _handle != IntPtr.Zero && GetWindow(_handle, GwOwner) == _owner.Handle;
    public string? LastError { get; private set; }

    public void UpdateContent(StickyNote note)
    {
        if (_closed) return;
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
        _alarmPulse.Update(note.IsAlarmRinging, StickyNoteAppearance.ResolveColor(note.Color).Alarm);
    }

    public bool TryShow()
    {
        if (_closed) return Fail("The passive window is closed.");
        LastError = null;
        var origin = _host.TranslatePoint(new WpfPoint(0, 0), _owner);
        _window.Left = _owner.Left + origin.X;
        _window.Top = _owner.Top + origin.Y;
        _window.Width = _host.ActualWidth;
        _window.Height = _host.ActualHeight;

        if (!_controller.IsNoteOnCurrentDesktop(_owner))
        {
            return Fail("The note is no longer on the current desktop.");
        }

        _root.Opacity = 0;
        _handle = new WindowInteropHelper(_window).EnsureHandle();
        if (_handle == IntPtr.Zero)
        {
            return Fail("Could not create the passive window handle.");
        }

        if (!TrySetInputStyles(requireLayered: false))
        {
            return false;
        }

        _window.Show();
        _bounds = null;
        if (!TrySetInputStyles(requireLayered: true))
        {
            return false;
        }

        if (!VerifyAffinity()) return false;

        if (!TryAlign())
        {
            return false;
        }

        _root.Opacity = 1;
        return true;
    }

    private bool TrySetInputStyles(bool requireLayered)
    {
        var style = GetWindowLong(_handle, GwlExStyle);
        var error = Marshal.GetLastPInvokeError();
        if (style == 0 && error != 0)
        {
            return Fail($"GetWindowLong failed (Win32 {error}).");
        }

        var desired = style | WsExTransparent | WsExNoActivate;
        if (desired != style)
        {
            var previous = SetWindowLong(_handle, GwlExStyle, desired);
            error = Marshal.GetLastPInvokeError();
            if (previous == 0 && error != 0)
            {
                return Fail($"SetWindowLong failed (Win32 {error}).");
            }
        }

        var required = WsExTransparent | WsExNoActivate | (requireLayered ? WsExLayered : 0);
        var actual = GetWindowLong(_handle, GwlExStyle);
        return (actual & required) == required
            || Fail($"Passive window styles were not retained (actual 0x{actual:X8}, required 0x{required:X8}).");
    }

    private bool Fail(string message)
    {
        LastError = message;
        return false;
    }

    public bool VerifyAffinity()
    {
        if (_closed) return Fail("The passive window is closed.");
        return _controller.IsPassiveBodyOnOwnerDesktop(this, _owner, out var reason) || Fail(reason);
    }

    public bool TryAlign()
    {
        if (_closed || _handle == IntPtr.Zero || _host.ActualWidth <= 0 || _host.ActualHeight <= 0)
        {
            return Fail("The note body has no usable window bounds.");
        }

        var topLeft = _host.PointToScreen(new WpfPoint(0, 0));
        var bottomRight = _host.PointToScreen(new WpfPoint(_host.ActualWidth, _host.ActualHeight));
        var x = (int)Math.Round(topLeft.X);
        var y = (int)Math.Round(topLeft.Y);
        var width = Math.Max(1, (int)Math.Round(bottomRight.X) - x);
        var height = Math.Max(1, (int)Math.Round(bottomRight.Y) - y);
        var bounds = (x, y, width, height);
        if (_bounds == bounds)
        {
            return true;
        }

        if (!VerifyAffinity()) return false;
        if (!SetWindowPos(_handle, IntPtr.Zero, x, y, width, height, SwpNoActivate | SwpNoZOrder))
        {
            return Fail($"SetWindowPos failed (Win32 {Marshal.GetLastPInvokeError()}).");
        }

        _bounds = bounds;
        return true;
    }

    public void Dispose()
    {
        if (_closed) return;
        _alarmPulse.Update(false, "");
        _window.Close();
    }

    private static SolidColorBrush BrushFor(string value) =>
        new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value)!);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr handle, uint command);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr handle, int index, int value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}

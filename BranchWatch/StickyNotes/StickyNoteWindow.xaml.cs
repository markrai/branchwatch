using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Input;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using WpfSeparator = System.Windows.Controls.Separator;
using WpfMessageBox = System.Windows.MessageBox;
using WpfColorConverter = System.Windows.Media.ColorConverter;

namespace BranchWatch;

public partial class StickyNoteWindow : Window
{
    private readonly StickyNote _note;
    private readonly StickyNotesController _controller;
    private bool _trackingBounds;
    private StickyNotePassiveBody? _passiveBody;
    private bool _desktopVerified;
    private bool _passiveBodyFailed;
    private bool _refreshingPassiveBody;
    private bool _closing;
    private StickyNoteResizeBounds? _shrinkStart;
    private NativePoint _shrinkCursor;
    private DpiScale _shrinkDpi;
    private readonly double _expandedMinHeight;
    private double _expandedHeight;
    private bool _changingCollapse;

    internal StickyNoteWindow(StickyNote note, StickyNotesController controller)
    {
        _note = note;
        _controller = controller;
        InitializeComponent();
        _expandedMinHeight = MinHeight;

        Editor.Text = note.Text;
        ApplyColor();
        ApplyBounds();
        _expandedHeight = Height;
        ApplyCollapsedLayout();
        ApplyTransparency(_note.TransparencyLevel);
        Editor.TextChanged += (_, _) =>
        {
            _note.Text = Editor.Text;
            _controller.MarkDirty();
            _passiveBody?.UpdateContent(_note);
        };
        LocationChanged += (_, _) => { CaptureBounds(); AlignPassiveBody(); };
        SizeChanged += (_, _) => { CaptureBounds(); AlignPassiveBody(); };
        StateChanged += (_, _) => RefreshPassiveBody();
        IsVisibleChanged += (_, _) => RefreshPassiveBody();
        Closing += (_, _) =>
        {
            _closing = true;
            RemovePassiveBody();
        };
        LayoutUpdated += (_, _) => AlignPassiveBody();
    }

    internal IntPtr Handle => new WindowInteropHelper(this).Handle;

    internal void RefreshDesktopName()
    {
        DesktopLabel.Text = _controller.DesktopName(_note.DesktopId);
        DesktopLabel.ToolTip = DesktopLabel.Text;
        Title = $"Sticky note – {DesktopLabel.Text}";
    }

    internal void FocusEditor()
    {
        Activate();
        if (!_note.IsCollapsed && _note.TransparencyLevel == StickyNoteTransparency.MinLevel)
        {
            Editor.Focus();
        }
        else
        {
            OptionsButton.Focus();
        }
    }

    internal void BeginTrackingBounds()
    {
        _trackingBounds = true;
        _note.EnsureOriginalSize();
        CaptureBounds();
        _desktopVerified = true;
        RefreshPassiveBody();
    }

    internal void CaptureBounds()
    {
        if (_changingCollapse || !_trackingBounds || !IsLoaded || WindowState != WindowState.Normal
            || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        _note.Left = Left;
        _note.Top = Top;
        _note.Width = ActualWidth;
        if (!_note.IsCollapsed)
        {
            _expandedHeight = ActualHeight;
            _note.Height = _expandedHeight;
        }
        _controller.MarkDirty();
    }

    private void ApplyBounds()
    {
        var work = SystemParameters.WorkArea;
        _note.EnsureOriginalSize();
        Width = Math.Clamp(double.IsFinite(_note.Width) ? _note.Width : StickyNote.DefaultWidth, MinWidth, Math.Max(MinWidth, work.Width));
        Height = Math.Clamp(double.IsFinite(_note.Height) ? _note.Height : StickyNote.DefaultHeight, MinHeight, Math.Max(MinHeight, work.Height));

        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var left = _note.Left;
        var top = _note.Top;
        if (!double.IsFinite(left) || !double.IsFinite(top)
            || !screen.Contains(new System.Windows.Point(left + 30, top + 15)))
        {
            left = work.Right - Width - 24;
            top = work.Top + 24;
        }

        Left = left;
        Top = top;
    }

    internal void SetCollapsed(bool collapsed)
    {
        if (_note.IsCollapsed == collapsed)
        {
            return;
        }

        _changingCollapse = true;
        try
        {
            if (collapsed)
            {
                _expandedHeight = Height;
                _note.Height = _expandedHeight;
                _shrinkStart = null;
                if (ShrinkGrip.IsDragging)
                {
                    ShrinkGrip.CancelDrag();
                }
                if (Editor.IsKeyboardFocusWithin)
                {
                    CollapseButton.Focus();
                }
            }

            _note.IsCollapsed = collapsed;
            if (collapsed)
            {
                RemovePassiveBody();
            }
            else
            {
                _passiveBodyFailed = false;
            }

            ApplyCollapsedLayout();
            UpdateLayout();
        }
        finally
        {
            _changingCollapse = false;
        }

        CaptureBounds();
        _controller.MarkDirty();
        RefreshPassiveBody();
    }

    private void ApplyCollapsedLayout()
    {
        var collapsed = _note.IsCollapsed;
        BodyHost.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        if (collapsed)
        {
            var headerHeight = HeaderRow.Height.Value + NoteSurface.BorderThickness.Top + NoteSurface.BorderThickness.Bottom;
            MinHeight = headerHeight;
            Height = headerHeight;
        }
        else
        {
            MinHeight = _expandedMinHeight;
            Height = Math.Max(_expandedMinHeight, _expandedHeight);
        }

        var label = collapsed ? "Expand note" : "Collapse note";
        CollapseButton.ToolTip = label;
        System.Windows.Automation.AutomationProperties.SetName(CollapseButton, label);
    }

    private void CollapseClick(object sender, RoutedEventArgs e) => SetCollapsed(!_note.IsCollapsed);

    private void ApplyColor()
    {
        var palette = StickyNoteAppearance.ResolveColor(_note.Color);
        NoteSurface.Background = BrushFor(palette.Body);
        HeaderSurface.Background = BrushFor(palette.Header);
        ShrinkGrip.Background = BrushFor(palette.Body);
        UpdateInputPresentation();
        _passiveBody?.UpdateContent(_note);
    }

    internal void ApplyAppearance(StickyNoteAppearance appearance)
    {
        appearance.ApplyTo(_note);
        ApplyColor();
        ApplyTransparency(_note.TransparencyLevel);
    }

    internal void ApplyTransparency(int level)
    {
        _note.TransparencyLevel = level;
        NoteSurface.Opacity = StickyNoteTransparency.OpacityForLevel(_note.TransparencyLevel);
        _passiveBodyFailed = false;
        UpdateInputPresentation();
        RefreshPassiveBody();
    }

    internal bool IsBodyClickThrough => _passiveBody is { IsVisible: true };

    private bool WantsClickThrough => _note.TransparencyLevel > StickyNoteTransparency.MinLevel;

    private void UpdateInputPresentation()
    {
        var passive = IsBodyClickThrough && WantsClickThrough;
        var palette = StickyNoteAppearance.ResolveColor(_note.Color);
        NoteSurface.Background = passive ? System.Windows.Media.Brushes.Transparent : BrushFor(palette.Body);
        NoteSurface.BorderBrush = passive ? System.Windows.Media.Brushes.Transparent : BrushFor("#33000000");
        Editor.Opacity = passive ? 0 : 1;
        Editor.IsHitTestVisible = !passive;
        Editor.IsReadOnly = WantsClickThrough;
        Editor.Focusable = !WantsClickThrough;
        if (WantsClickThrough && Editor.IsKeyboardFocusWithin)
        {
            OptionsButton.Focus();
        }
    }

    internal void RefreshPassiveBody()
    {
        if (!_desktopVerified || _closing || _changingCollapse || _refreshingPassiveBody)
        {
            return;
        }

        _refreshingPassiveBody = true;
        try
        {
            RefreshPassiveBodyCore();
        }
        finally
        {
            _refreshingPassiveBody = false;
        }
    }

    private void RefreshPassiveBodyCore()
    {
        if (_note.IsCollapsed || !WantsClickThrough || !IsVisible || WindowState == WindowState.Minimized
            || !_controller.IsNoteOnCurrentDesktop(this))
        {
            RemovePassiveBody();
            return;
        }

        if (_passiveBodyFailed || !IsLoaded || BodyHost.ActualWidth <= 0 || BodyHost.ActualHeight <= 0)
        {
            return;
        }

        if (_passiveBody is null)
        {
            var body = new StickyNotePassiveBody(this, _note);
            _passiveBody = body;
            if (!body.TryShow(BodyHost, _controller))
            {
                _passiveBodyFailed = true;
                var detail = body.LastError;
                RemovePassiveBody();
                _controller.ReportClickThroughFailure(detail);
                return;
            }

            UpdateInputPresentation();
        }
        else if (!_controller.IsPassiveBodyOnOwnerDesktop(_passiveBody, this, out _))
        {
            RemovePassiveBody();
            return;
        }

        AlignPassiveBodyCore();
        _passiveBody?.UpdateContent(_note);
    }

    private void AlignPassiveBody()
    {
        if (_closing || _changingCollapse || _note.IsCollapsed || _refreshingPassiveBody || _passiveBody is null)
        {
            return;
        }

        _refreshingPassiveBody = true;
        try
        {
            AlignPassiveBodyCore();
        }
        finally
        {
            _refreshingPassiveBody = false;
        }
    }

    private void AlignPassiveBodyCore()
    {
        if (_passiveBody is not null && IsVisible && WindowState != WindowState.Minimized
            && BodyHost.ActualWidth > 0 && BodyHost.ActualHeight > 0 && !_passiveBody.TryAlign(BodyHost))
        {
            _passiveBodyFailed = true;
            var detail = _passiveBody.LastError;
            RemovePassiveBody();
            _controller.ReportClickThroughFailure(detail);
        }
    }

    private void RemovePassiveBody()
    {
        if (_passiveBody is not { } body)
        {
            return;
        }

        _passiveBody = null;
        body.Close();
        if (!_closing)
        {
            UpdateInputPresentation();
        }
    }

    private static SolidColorBrush BrushFor(string value) =>
        new((System.Windows.Media.Color)WpfColorConverter.ConvertFromString(value)!);

    private void ShrinkDragStarted(object sender, DragStartedEventArgs e)
    {
        _shrinkStart = null;
        if (_note.IsCollapsed || WindowState != WindowState.Normal || !GetCursorPos(out _shrinkCursor))
        {
            return;
        }

        _shrinkDpi = VisualTreeHelper.GetDpi(this);
        _shrinkStart = new StickyNoteResizeBounds(Left, Top, ActualWidth, ActualHeight);
    }

    private void ShrinkDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_shrinkStart is not { } start || !GetCursorPos(out var cursor))
        {
            return;
        }

        var current = new StickyNoteResizeBounds(Left, Top, Width, Height);
        var next = StickyNoteResizeGeometry.Resize(start,
            (cursor.X - _shrinkCursor.X) / _shrinkDpi.DpiScaleX,
            (cursor.Y - _shrinkCursor.Y) / _shrinkDpi.DpiScaleY,
            MinWidth, MinHeight, _note.OriginalWidth ?? StickyNote.DefaultWidth,
            _note.OriginalHeight ?? StickyNote.DefaultHeight);
        if (next == current)
        {
            return;
        }

        Width = next.Width;
        Height = next.Height;
        Left = next.Left;
        UpdateLayout();
        CaptureBounds();
        AlignPassiveBody();
    }

    private void ShrinkDragCompleted(object sender, DragCompletedEventArgs e)
    {
        _shrinkStart = null;
        CaptureBounds();
        AlignPassiveBody();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    private void HeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void NewClick(object sender, RoutedEventArgs e) => _controller.CreateNote();

    private void CloseClick(object sender, RoutedEventArgs e) => Close();

    private void OptionsClick(object sender, RoutedEventArgs e)
    {
        var menu = new WpfContextMenu { PlacementTarget = OptionsButton };
        var colors = new WpfMenuItem { Header = "Color" };
        foreach (var color in StickyNoteAppearance.Palette)
        {
            var item = new WpfMenuItem { Header = color.Name, IsCheckable = true, IsChecked = _note.Color == color.Name };
            item.Click += (_, _) =>
            {
                _note.Color = color.Name;
                ApplyColor();
                _controller.MarkDirty();
            };
            colors.Items.Add(item);
        }

        menu.Items.Add(colors);
        var transparency = new WpfMenuItem { Header = $"Transparency ({_note.TransparencyLevel})" };
        transparency.ToolTip = "Levels 2–10 allow clicks through the body. Choose level 1 to edit.";
        for (var level = StickyNoteTransparency.MinLevel; level <= StickyNoteTransparency.MaxLevel; level++)
        {
            var selectedLevel = level;
            var label = level switch
            {
                StickyNoteTransparency.MinLevel => "1 (opaque & editable)",
                StickyNoteTransparency.MaxLevel => "10 (most transparent)",
                _ => level.ToString()
            };
            if (level == _controller.DefaultAppearance.TransparencyLevel)
            {
                label += " (default)";
            }

            var item = new WpfMenuItem
            {
                Header = label,
                IsCheckable = true,
                IsChecked = _note.TransparencyLevel == level
            };
            item.Click += (_, _) =>
            {
                ApplyTransparency(selectedLevel);
                _controller.MarkDirty();
            };
            transparency.Items.Add(item);
        }

        menu.Items.Add(transparency);
        var move = new WpfMenuItem { Header = "Move to desktop" };
        foreach (var desktop in _controller.Desktops)
        {
            var item = new WpfMenuItem
            {
                Header = desktop.DisplayName.Replace("_", "__"),
                IsCheckable = true,
                IsChecked = desktop.Id == _note.DesktopId,
                IsEnabled = desktop.Id != _note.DesktopId
            };
            item.Click += (_, _) => _controller.MoveNote(_note, desktop.Id);
            move.Items.Add(item);
        }

        move.IsEnabled = move.Items.Count > 0;
        menu.Items.Add(move);
        menu.Items.Add(new WpfSeparator());
        var delete = new WpfMenuItem { Header = "Delete note…" };
        delete.Click += (_, _) =>
        {
            if (WpfMessageBox.Show(this, "Permanently delete this note?", "Sticky notes",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes)
            {
                _controller.DeleteNote(_note);
            }
        };
        menu.Items.Add(delete);
        menu.IsOpen = true;
    }
}

using System.Windows;
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
    private static readonly (string Name, string Body, string Header)[] Palette =
    [
        ("Yellow", "#FFF5C5", "#F6DE87"),
        ("Green", "#DFF5D8", "#B8E2AD"),
        ("Pink", "#FCE1E8", "#F3BACB"),
        ("Blue", "#DDEEFF", "#B7D7F4"),
        ("Purple", "#EEE3FC", "#D8C3F1"),
        ("White", "#F5F5F5", "#DDDDDD")
    ];

    private readonly StickyNote _note;
    private readonly StickyNotesController _controller;
    private bool _trackingBounds;

    internal StickyNoteWindow(StickyNote note, StickyNotesController controller)
    {
        _note = note;
        _controller = controller;
        InitializeComponent();

        Editor.Text = note.Text;
        ApplyColor();
        ApplyBounds();
        SourceInitialized += (_, _) =>
        {
            if (!StickyNoteTransparency.TryApply(Handle, _note.TransparencyLevel))
            {
                _controller.ReportError("Windows could not apply the saved sticky note transparency.");
            }
        };
        Editor.TextChanged += (_, _) =>
        {
            _note.Text = Editor.Text;
            _controller.MarkDirty();
        };
        LocationChanged += (_, _) => CaptureBounds();
        SizeChanged += (_, _) => CaptureBounds();
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
        Editor.Focus();
    }

    internal void BeginTrackingBounds()
    {
        _trackingBounds = true;
        CaptureBounds();
    }

    internal void CaptureBounds()
    {
        if (!_trackingBounds || !IsLoaded || WindowState != WindowState.Normal
            || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        _note.Left = Left;
        _note.Top = Top;
        _note.Width = ActualWidth;
        _note.Height = ActualHeight;
        _controller.MarkDirty();
    }

    private void ApplyBounds()
    {
        var work = SystemParameters.WorkArea;
        Width = Math.Clamp(double.IsFinite(_note.Width) ? _note.Width : 300, MinWidth, Math.Max(MinWidth, work.Width));
        Height = Math.Clamp(double.IsFinite(_note.Height) ? _note.Height : 260, MinHeight, Math.Max(MinHeight, work.Height));

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

    private void ApplyColor()
    {
        var palette = Palette.FirstOrDefault(p => p.Name == _note.Color);
        if (palette.Name is null)
        {
            palette = Palette[0];
        }

        NoteSurface.Background = BrushFor(palette.Body);
        Header.Background = BrushFor(palette.Header);
        Background = NoteSurface.Background;
    }

    private static SolidColorBrush BrushFor(string value) =>
        new((System.Windows.Media.Color)WpfColorConverter.ConvertFromString(value)!);

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
        foreach (var color in Palette)
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
        for (var level = StickyNoteTransparency.MinLevel; level <= StickyNoteTransparency.MaxLevel; level++)
        {
            var selectedLevel = level;
            var label = level switch
            {
                StickyNoteTransparency.MinLevel => "1 (opaque)",
                StickyNoteTransparency.DefaultLevel => "3 (default)",
                StickyNoteTransparency.MaxLevel => "10 (most transparent)",
                _ => level.ToString()
            };
            var item = new WpfMenuItem
            {
                Header = label,
                IsCheckable = true,
                IsChecked = _note.TransparencyLevel == level
            };
            item.Click += (_, _) =>
            {
                if (StickyNoteTransparency.TryApply(Handle, selectedLevel))
                {
                    _note.TransparencyLevel = selectedLevel;
                    _controller.MarkDirty();
                }
                else
                {
                    _controller.ReportError("Windows could not change the sticky note transparency.");
                }
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

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Threading;
using System.Diagnostics;
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
    private readonly StickyNoteInteraction _interaction;
    private StickyNoteResizeBounds? _shrinkStart;
    private NativePoint _shrinkCursor;
    private DpiScale _shrinkDpi;
    private readonly double _expandedMinHeight;
    private double _expandedHeight;
    private bool _changingCollapse;
    private readonly DispatcherTimer _editTimer;
    private StickyNoteOutsideClickWatcher? _outsideClickWatcher;
    private System.Windows.Point? _titlePress;
    private readonly StickyNoteAlarmPulse _alarmPulse;

    internal StickyNoteWindow(StickyNote note, StickyNotesController controller)
    {
        _note = note;
        _controller = controller;
        _interaction = new StickyNoteInteraction(note);
        _editTimer = new DispatcherTimer(DispatcherPriority.Input, Dispatcher) { Interval = StickyNoteTemporaryEditing.Duration };
        _editTimer.Tick += (_, _) => ExpireTemporaryEditing();
        InitializeComponent();
        _alarmPulse = new StickyNoteAlarmPulse(AlarmOutline);
        _expandedMinHeight = MinHeight;

        Editor.Text = note.Text;
        TitleEditor.MaxLength = StickyNote.MaxTitleLength;
        RefreshDesktopName();
        PreviewKeyDown += NotePreviewKeyDown;
        ApplyColor();
        ApplyBounds();
        _expandedHeight = Height;
        ApplyCollapsedLayout();
        ApplyTransparency(_note.TransparencyLevel);
        RefreshAlarmPresentation();
        Editor.TextChanged += (_, _) =>
        {
            _note.Text = Editor.Text;
            _controller.MarkDirty();
            _passiveBody?.UpdateContent(_note);
        };
        LocationChanged += (_, _) => { CaptureBounds(); AlignPassiveBody(); };
        SizeChanged += (_, _) => { CaptureBounds(); AlignPassiveBody(); };
        Editor.PreviewMouseDown += (_, _) => HoldTemporaryEditing();
        Editor.GotKeyboardFocus += (_, _) => HoldTemporaryEditing();
        Deactivated += (_, _) =>
        {
            FinishRename(commit: true, focusHeader: false);
            ApplyInteractionChange(_interaction.Deactivate());
        };
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized)
            {
                FinishRename(commit: true, focusHeader: false);
                EndTemporaryEditing();
            }
            RefreshPassiveBody();
        };
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
            {
                FinishRename(commit: true, focusHeader: false);
                EndTemporaryEditing();
            }
            RefreshPassiveBody();
        };
        Closing += (_, _) =>
        {
            _interaction.BeginClosing();
            _alarmPulse.Update(false, StickyNoteAppearance.ResolveColor(_note.Color).Alarm);
            FinishRename(commit: true, focusHeader: false);
            EndTemporaryEditing();
            RemovePassiveBody();
        };
        LayoutUpdated += (_, _) => AlignPassiveBody();
    }

    internal IntPtr Handle => new WindowInteropHelper(this).Handle;

    internal void RefreshDesktopName()
    {
        var desktop = _controller.DesktopName(_note.DesktopId);
        DesktopLabel.Text = _note.DisplayTitle(desktop);
        DesktopLabel.ToolTip = $"{DesktopLabel.Text}\nDouble-click to rename" + (_note.CustomTitle is null ? "" : $"\nDesktop: {desktop}");
        Title = $"Sticky note – {DesktopLabel.Text}";
    }

    internal void FocusEditor()
    {
        if (_interaction.IsClosing) return;
        FinishRename(commit: true, focusHeader: false);
        Activate();
        if (_note.IsCollapsed)
        {
            OptionsButton.Focus();
            return;
        }

        BeginEditorSession();
        if (!Editor.Focus())
        {
            EndTemporaryEditing();
            return;
        }
        Editor.CaretIndex = Editor.Text.Length;
    }

    internal void BeginEditorSession()
    {
        ApplyInteractionChange(_interaction.BeginEditorSession());
    }

    internal void FinishEditing()
    {
        if (Editor.IsKeyboardFocusWithin) FocusHeader();
        EndTemporaryEditing();
    }

    private void FocusHeader()
    {
        if (Header.Focus()) return;
        Keyboard.ClearFocus();
        FocusManager.SetFocusedElement(this, null);
    }

    private void NotePreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_interaction.IsRenaming && TitleEditor.IsKeyboardFocusWithin && e.Key is Key.Enter or Key.Escape)
        {
            e.Handled = true;
            FinishRename(commit: e.Key == Key.Enter);
        }
        else if (e.Key == Key.Escape && (Editor.IsKeyboardFocusWithin || _interaction.IsTemporaryEditing))
        {
            e.Handled = true;
            FinishEditing();
        }
    }

    private void TitleMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        e.Handled = true;
        if (e.ClickCount == 2)
        {
            ReleaseTitlePress();
            BeginRename();
            return;
        }
        _titlePress = e.GetPosition(DesktopLabel);
        if (!DesktopLabel.CaptureMouse()) _titlePress = null;
    }

    private void TitleMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_titlePress is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
        var position = e.GetPosition(DesktopLabel);
        if (Math.Abs(position.X - start.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        ReleaseTitlePress();
        e.Handled = true;
        if (Mouse.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void TitleMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_titlePress is null) return;
        ReleaseTitlePress();
        e.Handled = true;
        if (DesktopLabel.IsMouseOver) HandleHeaderClick();
    }

    private void TitleLostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e) => _titlePress = null;

    private void ReleaseTitlePress()
    {
        _titlePress = null;
        if (DesktopLabel.IsMouseCaptured) DesktopLabel.ReleaseMouseCapture();
    }

    internal void BeginRename()
    {
        if (!_interaction.CanBeginRename) return;
        FinishEditing();
        var original = _note.DisplayTitle(_controller.DesktopName(_note.DesktopId));
        if (!_interaction.TryBeginRename(original)) return;
        TitleEditor.Text = original;
        DesktopLabel.Visibility = Visibility.Collapsed;
        TitleEditor.Visibility = Visibility.Visible;
        TitleEditor.Focus();
        TitleEditor.SelectAll();
    }

    internal void FinishRename(bool commit, bool focusHeader = true)
    {
        var result = _interaction.FinishRename(TitleEditor.Text, commit);
        if (result == StickyNoteRenameResult.None) return;
        if (result == StickyNoteRenameResult.Changed) _controller.MarkDirty();
        if (focusHeader && TitleEditor.IsKeyboardFocusWithin) FocusHeader();
        TitleEditor.Visibility = Visibility.Collapsed;
        DesktopLabel.Visibility = Visibility.Visible;
        RefreshDesktopName();
    }

    private void TitleEditorLostFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        FinishRename(commit: true, focusHeader: false);

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
                EndTemporaryEditing();
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
        RefreshAlarmPresentation();
    }

    internal void ApplyAppearance(StickyNoteAppearance appearance)
    {
        appearance.ApplyTo(_note);
        ApplyColor();
        ApplyTransparency(_note.TransparencyLevel);
    }

    internal void ApplyTransparency(int level)
    {
        StopTemporaryEditing();
        _note.TransparencyLevel = level;
        _passiveBodyFailed = false;
        RefreshTransparencyPresentation();
    }

    internal bool IsBodyClickThrough => _passiveBody is { IsVisible: true };

    private int EffectiveTransparencyLevel => _interaction.EffectiveTransparencyLevel;
    private bool WantsClickThrough => _interaction.WantsClickThrough;

    internal void ToggleTemporaryEditing() =>
        ApplyInteractionChange(_interaction.ToggleTemporaryEditing(Stopwatch.GetTimestamp()));

    internal void HoldTemporaryEditing() =>
        ApplyInteractionChange(_interaction.HoldTemporaryEditing(Stopwatch.GetTimestamp()));

    private void ApplyInteractionChange(StickyNoteInteractionChange change)
    {
        switch (change)
        {
            case StickyNoteInteractionChange.None:
                return;
            case StickyNoteInteractionChange.AlarmDismissed:
                _controller.AlarmChanged();
                RefreshAlarmPresentation();
                return;
            case StickyNoteInteractionChange.PreviewStarted:
                StopEditingResources();
                RefreshTransparencyPresentation();
                if (_interaction.IsPreview) _editTimer.Start();
                return;
            case StickyNoteInteractionChange.HeldStarted:
                StopEditingResources();
                RefreshTransparencyPresentation();
                if (_interaction.IsHeld) WatchOutsideClicks();
                return;
            case StickyNoteInteractionChange.PreviewHeld:
                _editTimer.Stop();
                WatchOutsideClicks();
                return;
            case StickyNoteInteractionChange.EditingEnded:
                StopEditingResources();
                if (_interaction.IsClosing) return;
                if (Editor.IsKeyboardFocusWithin) FocusHeader();
                RefreshTransparencyPresentation();
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(change));
        }
    }

    private void WatchOutsideClicks()
    {
        _outsideClickWatcher?.Dispose();
        _outsideClickWatcher = null;
        if (Handle != IntPtr.Zero)
        {
            _outsideClickWatcher = StickyNoteOutsideClickWatcher.TryStart(Handle, Dispatcher, () =>
            {
                if (_interaction.IsHeld) EndTemporaryEditing();
            });
        }
    }

    internal void ExpireTemporaryEditing()
    {
        _editTimer.Stop();
        ApplyInteractionChange(_interaction.ExpirePreview());
    }

    internal void EndTemporaryEditing()
    {
        var change = _interaction.EndEditing();
        if (change == StickyNoteInteractionChange.None) StopEditingResources();
        else ApplyInteractionChange(change);
    }

    private bool StopTemporaryEditing()
    {
        StopEditingResources();
        return _interaction.EndEditing() == StickyNoteInteractionChange.EditingEnded;
    }

    private void StopEditingResources()
    {
        _editTimer.Stop();
        _outsideClickWatcher?.Dispose();
        _outsideClickWatcher = null;
    }

    private void RefreshTransparencyPresentation()
    {
        NoteSurface.Opacity = StickyNoteTransparency.OpacityForLevel(EffectiveTransparencyLevel);
        if (!WantsClickThrough) RemovePassiveBody();
        UpdateInputPresentation();
        RefreshPassiveBody();
    }

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
        RefreshAlarmPresentation();
    }

    internal void RefreshPassiveBody()
    {
        if (_desktopVerified && !_interaction.IsClosing && _interaction.IsTemporaryEditing && !_controller.IsNoteOnCurrentDesktop(this))
        {
            if (StopTemporaryEditing())
            {
                NoteSurface.Opacity = StickyNoteTransparency.OpacityForLevel(EffectiveTransparencyLevel);
                UpdateInputPresentation();
            }
        }
        if (!_desktopVerified || _interaction.IsClosing || _changingCollapse || _refreshingPassiveBody)
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
        if (_interaction.IsClosing || _changingCollapse || _note.IsCollapsed || _refreshingPassiveBody || _passiveBody is null)
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
        if (!_interaction.IsClosing)
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
        if (e.Handled || e.ButtonState != MouseButtonState.Pressed || (_interaction.IsRenaming && TitleEditor.IsMouseOver)) return;
        var left = Left;
        var top = Top;
        var hasStart = GetCursorPos(out var start);
        DragMove();
        if (hasStart && GetCursorPos(out var end) && Left == left && Top == top
            && Math.Abs((long)end.X - start.X) < SystemParameters.MinimumHorizontalDragDistance * VisualTreeHelper.GetDpi(this).DpiScaleX
            && Math.Abs((long)end.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance * VisualTreeHelper.GetDpi(this).DpiScaleY)
        {
            HandleHeaderClick();
        }
    }

    internal void HandleHeaderClick()
    {
        ApplyInteractionChange(_interaction.HeaderClick(DateTimeOffset.UtcNow, Stopwatch.GetTimestamp()));
    }

    private void AlarmClick(object sender, RoutedEventArgs e)
    {
        if (!_interaction.TryBeginAlarmDialog()) return;
        try
        {
            FinishEditing();
            var dialog = new StickyNoteAlarmDialog(_note.AlarmDueUtc, _note.IsAlarmRinging) { Owner = this, Topmost = true };
            if (dialog.ShowDialog() != true) return;
            if (dialog.RemoveAlarm)
            {
                if (_note.ClearAlarm()) _controller.AlarmChanged();
            }
            else if (dialog.SelectedDueUtc is { } due && _note.SetAlarm(due, DateTimeOffset.UtcNow))
                _controller.AlarmChanged();
            else
                WpfMessageBox.Show(this, "Choose a future alarm time.", "Sticky note alarm", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            _interaction.EndAlarmDialog();
            RefreshAlarmPresentation();
        }
    }

    internal void RefreshAlarmPresentation()
    {
        if (_interaction.IsClosing) return;
        var ringing = _note.IsAlarmRinging;
        AlarmDot.Visibility = _note.AlarmDueUtc.HasValue ? Visibility.Visible : Visibility.Collapsed;
        var label = ringing ? "Alarm due · click header to dismiss"
            : _note.AlarmDueUtc is { } due ? $"Alarm set · {StickyNoteAlarmTime.FormatLocal(due)}" : "Set alarm";
        AlarmButton.ToolTip = label;
        System.Windows.Automation.AutomationProperties.SetName(AlarmButton, label);
        var passive = IsBodyClickThrough && WantsClickThrough;
        AlarmOverlay.VerticalAlignment = passive ? VerticalAlignment.Top : VerticalAlignment.Stretch;
        AlarmOverlay.Height = passive ? HeaderRow.Height.Value : double.NaN;
        AlarmOutline.BorderThickness = passive ? new Thickness(3, 3, 3, 0) : new Thickness(3);
        _alarmPulse.Update(ringing, StickyNoteAppearance.ResolveColor(_note.Color).Alarm);
        _passiveBody?.UpdateContent(_note);
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
        transparency.ToolTip = "Levels 2–10 allow clicks through the body. Click the note header for temporary editing, or choose level 1 to keep it editable.";
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

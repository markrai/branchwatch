using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WpfCanvas = System.Windows.Controls.Canvas;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace BranchWatch;

public partial class DesktopMemorySettingsPage : System.Windows.Controls.UserControl
{
    private sealed record DesktopChoice(Guid Id, string Label);
    private sealed record DetailRow(string Label, string Detail);
    private readonly DesktopMemoryStore _store;
    private IReadOnlyList<DesktopSnapshot> _saved = [];
    private DesktopSnapshot? _draft;
    private Guid? _active;
    private bool _updating;
    private bool _busy;
    private bool _stopped;
    private bool _storageAvailable;

    internal DesktopMemorySettingsPage(DesktopMemoryStore store)
    {
        _store = store;
        InitializeComponent();
        Reload();
        RefreshDesktops();
    }

    internal void Stop() => _stopped = true;

    internal void RefreshDesktops()
    {
        if (_stopped || _busy) return;
        var selected = SelectedId;
        var state = VirtualDesktopRegistryReader.TryGetDisplayState();
        _active = VirtualDesktopRegistryReader.TryGetVerifiedDisplayState()?.ActiveDesktop.Id;
        var choices = (state?.AllDesktops ?? Array.Empty<VirtualDesktopInfo>())
            .Select(d => new DesktopChoice(d.Id, d.DisplayName + (d.Id == _active ? " (active)" : ""))).ToList();
        foreach (var snapshot in _saved.Where(s => choices.All(c => c.Id != s.DesktopId)))
            choices.Add(new DesktopChoice(snapshot.DesktopId, snapshot.DesktopName + " (unavailable)"));

        _updating = true;
        try
        {
            DesktopPicker.ItemsSource = choices;
            DesktopPicker.SelectedValue = selected is { } id && choices.Any(c => c.Id == id) ? id
                : _active is { } active && choices.Any(c => c.Id == active) ? active
                : choices.FirstOrDefault()?.Id;
        }
        finally { _updating = false; }
        Render();
    }

    private Guid? SelectedId => DesktopPicker.SelectedValue is Guid id ? id : null;
    private DesktopSnapshot? SelectedSaved => _saved.SingleOrDefault(s => s.DesktopId == SelectedId);

    private void Reload()
    {
        try
        {
            _saved = _store.Load();
            _storageAvailable = true;
        }
        catch (Exception ex)
        {
            _saved = [];
            _storageAvailable = false;
            StatusText.Text = $"Snapshot storage could not be read. No changes will be made. {ex.Message}";
        }
    }

    private void OnDesktopSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;
        Render();
    }

    private void OnRefresh(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        Reload();
        if (_storageAvailable) StatusText.Text = "Snapshot list refreshed.";
        RefreshDesktops();
    }

    private async void OnCapture(object sender, RoutedEventArgs e)
    {
        if (_busy || !_storageAvailable || SelectedId is not { } id || id != _active) return;
        if (_draft is not null && !Confirm("Discard the unsaved capture and take a new one?", "Capture layout")) return;
        _draft = null;
        _busy = true;
        StatusText.Text = "Inspecting application windows and monitors...";
        Render();
        try
        {
            var result = await CaptureOnSta(id);
            if (_stopped) return;
            _draft = result;
            StatusText.Text = result.Windows.Count == 0 ? "No eligible windows captured. Nothing was saved."
                : "Review the capture below, then save it when ready.";
        }
        catch (Exception ex)
        {
            if (!_stopped) StatusText.Text = $"Capture failed: {ex.Message}";
        }
        finally
        {
            _busy = false;
            if (!_stopped) RefreshDesktops();
        }
    }

    private static Task<DesktopSnapshot> CaptureOnSta(Guid id)
    {
        var completion = new TaskCompletionSource<DesktopSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(DesktopSnapshotCapture.Capture(id)); }
            catch (Exception ex) { completion.SetException(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_busy || !_storageAvailable || _draft is not { } draft || draft.DesktopId != SelectedId
            || draft.Windows.Count == 0) return;
        var existing = SelectedSaved;
        if (existing is not null && !Confirm("Replace this desktop's saved snapshot with the new capture?", "Replace snapshot")) return;
        try
        {
            _store.Save(draft, existing?.CapturedUtc);
            _draft = null;
            Reload();
            if (_storageAvailable) StatusText.Text = "Snapshot saved locally.";
        }
        catch (Exception ex) { StatusText.Text = $"Snapshot was not saved: {ex.Message}"; }
        RefreshDesktops();
    }

    private void OnDiscard(object sender, RoutedEventArgs e)
    {
        _draft = null;
        StatusText.Text = "Unsaved capture discarded.";
        Render();
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_busy || !_storageAvailable || SelectedSaved is not { } saved
            || !Confirm("Delete this desktop's saved snapshot? Your open applications will not be affected.", "Delete snapshot")) return;
        try
        {
            _store.Delete(saved.DesktopId, saved.CapturedUtc);
            Reload();
            if (_storageAvailable) StatusText.Text = "Snapshot deleted. The local recovery backup may retain an older copy.";
        }
        catch (Exception ex) { StatusText.Text = $"Snapshot was not deleted: {ex.Message}"; }
        RefreshDesktops();
    }

    private bool Confirm(string text, string title) => System.Windows.MessageBox.Show(
        Window.GetWindow(this), text, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    private void Render()
    {
        if (_stopped) return;
        var draft = _draft?.DesktopId == SelectedId ? _draft : null;
        var snapshot = draft ?? SelectedSaved;
        DesktopPicker.IsEnabled = !_busy;
        CaptureButton.IsEnabled = !_busy && _storageAvailable && SelectedId.HasValue && SelectedId == _active;
        CaptureHint.Text = _active is null ? "Windows could not verify the active desktop. Capture is unavailable."
            : SelectedId == _active ? "Arrange your applications before taking a capture."
            : "Switch to the selected desktop before taking a capture.";
        DeleteButton.IsEnabled = !_busy && _storageAvailable && SelectedSaved is not null;
        SaveButton.Visibility = DiscardButton.Visibility = draft is null ? Visibility.Collapsed : Visibility.Visible;
        SaveButton.IsEnabled = !_busy && _storageAvailable && draft?.Windows.Count > 0;
        DiscardButton.IsEnabled = !_busy;
        SnapshotCard.Visibility = snapshot is null ? Visibility.Collapsed : Visibility.Visible;
        MonitorCanvas.Children.Clear();
        if (snapshot is null)
        {
            MonitorRows.ItemsSource = null;
            WindowRows.ItemsSource = null;
            return;
        }

        SnapshotHeading.Text = draft is null ? "Saved snapshot" : "Unsaved capture";
        SnapshotSummary.Text = $"{snapshot.CapturedUtc.ToLocalTime():g} · {snapshot.Monitors.Count} monitors · {snapshot.Windows.Count} windows";
        WarningsText.Text = string.Join(Environment.NewLine, snapshot.Warnings);
        MonitorRows.ItemsSource = snapshot.Monitors.Select((m, i) => new DetailRow(
            $"Monitor {i + 1}{(m.Primary ? " · Primary" : "")}",
            $"{m.Bounds.Width} × {m.Bounds.Height} px · Rotation {m.RotationDegrees?.ToString() ?? "unknown"}° · Scale {m.ScalePercent?.ToString() ?? "unknown"}% · {m.SourceName}")).ToArray();
        WindowRows.ItemsSource = snapshot.Windows.OrderBy(w => w.EnumerationIndex).Select(w => new DetailRow(
            w.Title ?? w.ProcessName ?? "Unknown application",
            $"{w.ProcessName ?? "Unknown process"} · {w.MonitorSource} · {(w.Minimized ? "Minimized" : w.Maximized ? "Maximized" : "Normal")} · {w.NormalPlacementBounds.Width} × {w.NormalPlacementBounds.Height}")).ToArray();
        DrawLayout(snapshot);
    }

    private void DrawLayout(DesktopSnapshot snapshot)
    {
        var left = snapshot.Monitors.Min(m => m.Bounds.Left);
        var top = snapshot.Monitors.Min(m => m.Bounds.Top);
        var right = snapshot.Monitors.Max(m => m.Bounds.Right);
        var bottom = snapshot.Monitors.Max(m => m.Bounds.Bottom);
        var scale = Math.Min(400.0 / ((long)right - left), 190.0 / ((long)bottom - top));
        var offsetX = (420 - ((long)right - left) * scale) / 2;
        var offsetY = (210 - ((long)bottom - top) * scale) / 2;
        foreach (var (monitor, index) in snapshot.Monitors.Select((m, i) => (m, i)))
        {
            var bounds = monitor.Bounds;
            var x = offsetX + ((long)bounds.Left - left) * scale;
            var y = offsetY + ((long)bounds.Top - top) * scale;
            var width = bounds.Width * scale;
            var height = bounds.Height * scale;
            AddRectangle(x, y, width, height, "#263141", "#8998AA");
            foreach (var window in snapshot.Windows.Where(w => !w.Minimized).OrderByDescending(w => w.EnumerationIndex))
            {
                var b = window.VisibleFrameBounds ?? window.ScreenBounds;
                var x1 = Math.Max(b.Left, bounds.Left);
                var y1 = Math.Max(b.Top, bounds.Top);
                var x2 = Math.Min(b.Right, bounds.Right);
                var y2 = Math.Min(b.Bottom, bounds.Bottom);
                if (x2 <= x1 || y2 <= y1) continue;
                AddRectangle(offsetX + ((long)x1 - left) * scale, offsetY + ((long)y1 - top) * scale,
                    ((long)x2 - x1) * scale, ((long)y2 - y1) * scale, "#526B8B", "#B4C8E1");
            }
            var label = new WpfTextBlock { Text = $"{index + 1}{(monitor.Primary ? " ★" : "")}", Foreground = System.Windows.Media.Brushes.White,
                Background = Brush("#263141"), Padding = new Thickness(3, 1, 3, 1), FontSize = 11, IsHitTestVisible = false };
            WpfCanvas.SetLeft(label, x + 2);
            WpfCanvas.SetTop(label, y + 2);
            MonitorCanvas.Children.Add(label);
        }
    }

    private void AddRectangle(double x, double y, double width, double height, string fill, string stroke)
    {
        var rect = new WpfRectangle { Width = width, Height = height, Fill = Brush(fill), Stroke = Brush(stroke), StrokeThickness = 0.7, RadiusX = 2, RadiusY = 2 };
        WpfCanvas.SetLeft(rect, x);
        WpfCanvas.SetTop(rect, y);
        MonitorCanvas.Children.Add(rect);
    }

    private static SolidColorBrush Brush(string hex) => new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)!);
}

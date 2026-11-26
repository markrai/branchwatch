using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
    private CurrentDisplayTopology? _previewTopology;
    private Guid? _previewDesktop;
    private DateTimeOffset? _previewCapture;
    private DesktopPlacementPlan? _reviewedPlan;
    private CancellationTokenSource? _restoreCancellation;
    private Guid? _resultDesktop;

    internal DesktopMemorySettingsPage(DesktopMemoryStore store)
    {
        _store = store;
        InitializeComponent();
        Reload();
        RefreshDesktops();
    }

    internal void Stop()
    {
        _stopped = true;
        _restoreCancellation?.Cancel();
    }

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
        ClearResults();
        ClearPreview();
        Render();
    }

    private void OnRefresh(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        ClearPreview();
        Reload();
        if (_storageAvailable) StatusText.Text = "Snapshot list refreshed.";
        RefreshDesktops();
    }

    private async void OnCapture(object sender, RoutedEventArgs e)
    {
        if (_busy || !_storageAvailable || SelectedId is not { } id || id != _active) return;
        if (_draft is not null && !Confirm("Discard the unsaved capture and take a new one?", "Capture layout")) return;
        ClearPreview();
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
            ClearPreview();
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
            ClearPreview();
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
        if (_resultDesktop != SelectedId && !_busy) ClearResults();
        var draft = _draft?.DesktopId == SelectedId ? _draft : null;
        var snapshot = draft ?? SelectedSaved;
        DesktopPicker.IsEnabled = !_busy;
        CaptureButton.IsEnabled = !_busy && _storageAvailable && SelectedId.HasValue && SelectedId == _active;
        CaptureHint.Text = _active is null ? "Windows could not verify the active desktop. Capture is unavailable."
            : SelectedId == _active ? "Arrange your applications before taking a capture."
            : "Switch to the selected desktop before taking a capture.";
        PreviewButton.IsEnabled = !_busy && _storageAvailable && draft is null && SelectedSaved is not null;
        if (!PreviewButton.IsEnabled || _previewDesktop != SelectedId || _previewCapture != SelectedSaved?.CapturedUtc)
            ClearPreview();
        UpdateRestoreAvailability();
        DeleteButton.IsEnabled = !_busy && _storageAvailable && SelectedSaved is not null;
        SaveButton.Visibility = DiscardButton.Visibility = draft is null ? Visibility.Collapsed : Visibility.Visible;
        SaveButton.IsEnabled = !_busy && _storageAvailable && draft is { Windows.Count: > 0 };
        DiscardButton.IsEnabled = !_busy;
        SnapshotCard.Visibility = snapshot is null ? Visibility.Collapsed : Visibility.Visible;
        EmptyCard.Visibility = snapshot is null ? Visibility.Visible : Visibility.Collapsed;
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

    private void OnPreview(object sender, RoutedEventArgs e)
    {
        if (_busy || !_storageAvailable || SelectedSaved is not { } saved || _draft?.DesktopId == saved.DesktopId) return;
        var previousPolicy = _previewDesktop == saved.DesktopId && _previewCapture == saved.CapturedUtc
            ? FallbackPicker.SelectedIndex : 0;
        ClearPreview();
        FallbackPicker.SelectedIndex = previousPolicy;
        try
        {
            _previewTopology = DesktopMemoryProbe.CaptureDisplays();
            _previewDesktop = saved.DesktopId;
            _previewCapture = saved.CapturedUtc;
            RenderPlan(saved);
            StatusText.Text = "Preview calculated. No windows or display settings were changed.";
        }
        catch (Exception ex)
        {
            ClearPreview();
            StatusText.Text = $"Preview unavailable: {ex.Message}";
        }
    }

    private void OnFallbackChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_previewTopology is null || SelectedSaved is not { } saved || saved.DesktopId != _previewDesktop
            || saved.CapturedUtc != _previewCapture) return;
        try { RenderPlan(saved); }
        catch (Exception ex)
        {
            ClearPreview();
            StatusText.Text = $"Preview unavailable: {ex.Message}";
        }
    }

    private void ClearPreview()
    {
        _reviewedPlan = null;
        _previewTopology = null;
        _previewDesktop = null;
        _previewCapture = null;
        FallbackPicker.SelectedIndex = 0;
        PlanCard.Visibility = Visibility.Collapsed;
        PlanCanvas.Children.Clear();
        MatchRows.ItemsSource = null;
        PlanRows.ItemsSource = null;
    }

    private void RenderPlan(DesktopSnapshot saved)
    {
        var topology = _previewTopology!;
        var policy = FallbackPicker.SelectedIndex == 1 ? UnmatchedMonitorPolicy.Primary : UnmatchedMonitorPolicy.Skip;
        var plan = DesktopPlacementPlanner.Plan(saved, topology, policy);
        _reviewedPlan = plan;
        var skipped = plan.Windows.Count(w => w.Kind == WindowPlacementKind.Skipped);
        var review = plan.Windows.Count(w => w.RequiresReview && w.Kind != WindowPlacementKind.Skipped);
        PlanSummary.Text = $"{plan.Windows.Count - skipped} proposed · {skipped} skipped · {review} need review · checked {topology.ObservedUtc.ToLocalTime():g}";
        MatchRows.ItemsSource = plan.MonitorMatches.Select(m => new DetailRow(m.SavedSource,
            m.CurrentSource is { } target ? $"Matched physical monitor: {target}" : m.Kind switch
            {
                MonitorMatchKind.Missing => "Monitor not detected",
                MonitorMatchKind.Ambiguous => "Ambiguous physical identity",
                _ => "Physical identity unavailable"
            })).ToArray();
        var byId = saved.Windows.ToDictionary(w => w.Id);
        PlanRows.ItemsSource = plan.Windows.Select(w => new DetailRow(
            byId[w.WindowId].Title ?? byId[w.WindowId].ProcessName ?? "Unknown application",
            $"{w.Kind} · {w.TargetSource ?? "No target"} · {w.State}{(w.RequiresReview ? " · Review required" : "")}"
            + (w.ScreenBounds is { } b ? $" · ({b.Left}, {b.Top}) · {b.Width} × {b.Height} px" : "")
            + Environment.NewLine + w.Reason)).ToArray();
        PlanWarnings.Text = string.Join(Environment.NewLine, topology.Warnings.Append(
            "Preview reflects the displays at the time shown. Refresh after changing your monitor configuration. Running window identity and normal state will be verified before any move."));
        DrawLayout(PlanCanvas, topology.Monitors, plan.Windows.Reverse()
            .Where(w => w.ScreenBounds.HasValue && w.State != PlannedWindowState.Minimized)
            .Select(w => w.ScreenBounds!.Value).ToArray());
        PlanCard.Visibility = Visibility.Visible;
        UpdateRestoreAvailability();
    }

    private void UpdateRestoreAvailability()
    {
        RestoreButton.IsEnabled = !_busy && _storageAvailable && _reviewedPlan is not null
            && SelectedId.HasValue && SelectedId == _active
            && _reviewedPlan.Windows.Any(w => w.State == PlannedWindowState.Normal && w.Kind != WindowPlacementKind.Skipped);
        RestoreHint.Text = SelectedId != _active || _active is null
            ? "Switch to the snapshot's original desktop before restoring."
            : "Only uniquely matched, already-running normal windows are eligible. No windows will be launched, closed or activated.";
    }

    private void ClearResults()
    {
        _resultDesktop = null;
        RestoreResultsCard.Visibility = Visibility.Collapsed;
        RestoreRows.ItemsSource = null;
    }

    private void OnCancelRestore(object sender, RoutedEventArgs e)
    {
        _restoreCancellation?.Cancel();
        CancelRestoreButton.IsEnabled = false;
        RestoreSummary.Text = "Cancelling remaining moves...";
    }

    private async void OnRestore(object sender, RoutedEventArgs e)
    {
        if (_busy || !_storageAvailable || _reviewedPlan is not { } reviewed || SelectedSaved is not { } saved
            || SelectedId != _active || saved.DesktopId != reviewed.DesktopId || saved.CapturedUtc != reviewed.SnapshotCapturedUtc)
            return;
        var policy = FallbackPicker.SelectedIndex == 1 ? UnmatchedMonitorPolicy.Primary : UnmatchedMonitorPolicy.Skip;
        ClearResults();
        _resultDesktop = saved.DesktopId;
        RestoreResultsCard.Visibility = Visibility.Visible;
        RestoreSummary.Text = "Checking running windows...";
        var cancellation = new CancellationTokenSource();
        _restoreCancellation = cancellation;
        CancelRestoreButton.Visibility = Visibility.Visible;
        CancelRestoreButton.IsEnabled = true;
        _busy = true;
        Render();
        var service = new ExistingWindowRestoreService(new NativeExistingWindowRestoreEnvironment());
        try
        {
            VerifySavedSnapshot(saved);
            var approved = await RunOnSta(() => service.Prepare(saved, reviewed, policy, cancellation.Token));
            if (_stopped || cancellation.IsCancellationRequested) return;
            RestoreRows.ItemsSource = approved.Items.Select(i => new DetailRow(i.Label, $"{i.Status} · {i.Detail}")).ToArray();
            var ready = approved.Items.Where(i => i.Status == ExistingRestoreStatus.Ready).ToArray();
            RestoreSummary.Text = $"{ready.Length} matched · {approved.Items.Count - ready.Length} not eligible";
            if (ready.Length == 0) return;
            var names = string.Join(Environment.NewLine, ready.Take(8).Select(i => $"• {i.Label}"));
            if (ready.Length > 8) names += $"{Environment.NewLine}… and {ready.Length - 8} more";
            if (!Confirm($"Reposition {ready.Length} matching running windows on this desktop?{Environment.NewLine}{Environment.NewLine}{names}{Environment.NewLine}{Environment.NewLine}Other windows will not be intentionally moved. No applications will be launched. Geometry changes may be partial and are not automatically undone.", "Restore existing windows"))
            {
                RestoreSummary.Text = "Restore cancelled. No placement requests issued.";
                return;
            }

            if (_stopped || cancellation.IsCancellationRequested) return;
            VerifySavedSnapshot(saved);
            RestoreSummary.Text = "Restoring matched windows...";
            var results = await RunOnSta(() => service.Execute(approved, cancellation.Token));
            if (_stopped) return;
            RestoreRows.ItemsSource = results.Select(i => new DetailRow(i.Label, $"{i.Status} · {i.Detail}")).ToArray();
            RestoreSummary.Text = $"{results.Count(i => i.Status == ExistingRestoreStatus.Restored)} restored · "
                + $"{results.Count(i => i.Status == ExistingRestoreStatus.AlreadyPlaced)} already placed · "
                + $"{results.Count(i => i.Status is not (ExistingRestoreStatus.Restored or ExistingRestoreStatus.AlreadyPlaced))} not completed";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!_stopped) RestoreSummary.Text = "Cancelled.";
        }
        catch (Exception ex)
        {
            if (!_stopped) RestoreSummary.Text = $"Restore stopped: {ex.Message}";
        }
        finally
        {
            _restoreCancellation = null;
            cancellation.Dispose();
            _busy = false;
            if (!_stopped)
            {
                CancelRestoreButton.Visibility = Visibility.Collapsed;
                RefreshDesktops();
            }
        }
    }

    private void VerifySavedSnapshot(DesktopSnapshot saved)
    {
        var current = _store.Load().SingleOrDefault(s => s.DesktopId == saved.DesktopId);
        if (current is null || System.Text.Json.JsonSerializer.Serialize(current) != System.Text.Json.JsonSerializer.Serialize(saved))
            throw new InvalidOperationException("The saved snapshot changed. Refresh before restoring.");
    }

    private static Task<T> RunOnSta<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(work()); }
            catch (Exception ex) { completion.SetException(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private void DrawLayout(DesktopSnapshot snapshot) =>
        DrawLayout(MonitorCanvas, snapshot.Monitors, snapshot.Windows.Where(w => !w.Minimized)
            .OrderByDescending(w => w.EnumerationIndex).Select(w => w.VisibleFrameBounds ?? w.ScreenBounds).ToArray());

    private static void DrawLayout(WpfCanvas canvas, IReadOnlyList<SnapshotMonitor> monitors, IReadOnlyList<ProbeRect> windows)
    {
        canvas.Children.Clear();
        var left = monitors.Min(m => m.Bounds.Left);
        var top = monitors.Min(m => m.Bounds.Top);
        var right = monitors.Max(m => m.Bounds.Right);
        var bottom = monitors.Max(m => m.Bounds.Bottom);
        var scale = Math.Min(400.0 / ((long)right - left), 190.0 / ((long)bottom - top));
        var offsetX = (420 - ((long)right - left) * scale) / 2;
        var offsetY = (210 - ((long)bottom - top) * scale) / 2;
        foreach (var (monitor, index) in monitors.Select((m, i) => (m, i)))
        {
            var bounds = monitor.Bounds;
            var x = offsetX + ((long)bounds.Left - left) * scale;
            var y = offsetY + ((long)bounds.Top - top) * scale;
            var width = bounds.Width * scale;
            var height = bounds.Height * scale;
            AddRectangle(canvas, x, y, width, height, "#263141", "#8998AA");
            foreach (var b in windows)
            {
                var x1 = Math.Max(b.Left, bounds.Left);
                var y1 = Math.Max(b.Top, bounds.Top);
                var x2 = Math.Min(b.Right, bounds.Right);
                var y2 = Math.Min(b.Bottom, bounds.Bottom);
                if (x2 <= x1 || y2 <= y1) continue;
                AddRectangle(canvas, offsetX + ((long)x1 - left) * scale, offsetY + ((long)y1 - top) * scale,
                    ((long)x2 - x1) * scale, ((long)y2 - y1) * scale, "#526B8B", "#B4C8E1");
            }
            var label = new WpfTextBlock { Text = $"{index + 1}{(monitor.Primary ? " ★" : "")}", Foreground = System.Windows.Media.Brushes.White,
                Background = Brush("#263141"), Padding = new Thickness(3, 1, 3, 1), FontSize = 11, IsHitTestVisible = false };
            WpfCanvas.SetLeft(label, x + 2);
            WpfCanvas.SetTop(label, y + 2);
            canvas.Children.Add(label);
        }
    }

    private static void AddRectangle(WpfCanvas canvas, double x, double y, double width, double height, string fill, string stroke)
    {
        var rect = new WpfRectangle { Width = width, Height = height, Fill = Brush(fill), Stroke = Brush(stroke), StrokeThickness = 0.7, RadiusX = 2, RadiusY = 2 };
        WpfCanvas.SetLeft(rect, x);
        WpfCanvas.SetTop(rect, y);
        canvas.Children.Add(rect);
    }

    private static SolidColorBrush Brush(string hex) => new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)!);
}

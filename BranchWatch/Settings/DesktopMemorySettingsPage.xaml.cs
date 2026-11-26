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
    private sealed record FolderWindowChoice(Guid Id, string Label);
    private sealed class PlanWindowChoice(Guid id, string label, string detail, bool eligible, bool selected)
    {
        internal Guid Id { get; } = id;
        public string Label { get; } = label;
        public string Detail { get; } = detail;
        public bool Eligible { get; } = eligible;
        public bool Selected { get; set; } = selected;
    }
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
    private PlanWindowChoice[] _planChoices = [];

    internal DesktopMemorySettingsPage(DesktopMemoryStore store)
    {
        _store = store;
        InitializeComponent();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) _restoreCancellation?.Cancel();
        };
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
            InventoryExpander.IsExpanded = true;
            SnapshotCard.BringIntoView();
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
            RenderFolderEditor(null);
            MonitorRows.ItemsSource = null;
            WindowRows.ItemsSource = null;
            return;
        }

        SnapshotHeading.Text = draft is null ? "2 · Saved snapshot" : "2 · Review unsaved capture";
        SaveButton.Content = SelectedSaved is null ? "Save snapshot" : "Replace snapshot";
        SnapshotSummary.Text = $"{snapshot.CapturedUtc.ToLocalTime():g} · {snapshot.Monitors.Count} monitors · {snapshot.Windows.Count} windows";
        WarningsText.Text = string.Join(Environment.NewLine, snapshot.Warnings);
        RenderFolderEditor(draft);
        MonitorRows.ItemsSource = snapshot.Monitors.Select((m, i) => new DetailRow(
            $"Monitor {i + 1}{(m.Primary ? " · Primary" : "")}",
            $"{m.Bounds.Width} × {m.Bounds.Height} px · Rotation {m.RotationDegrees?.ToString() ?? "unknown"}° · Scale {m.ScalePercent?.ToString() ?? "unknown"}% · {m.SourceName}")).ToArray();
        WindowRows.ItemsSource = snapshot.Windows.OrderBy(w => w.EnumerationIndex).Select(w => new DetailRow(
            w.Title ?? w.ProcessName ?? "Unknown application",
            $"{w.ProcessName ?? "Unknown process"} · {w.MonitorSource} · {(w.Minimized ? "Minimized" : w.Maximized ? "Maximized" : "Normal")} · {w.NormalPlacementBounds.Width} × {w.NormalPlacementBounds.Height}" + (w.FolderContext is { } context ? $"{Environment.NewLine}Folder: {context.Folder}" : ""))).ToArray();
        DrawLayout(snapshot);
    }

    private void RenderFolderEditor(DesktopSnapshot? draft)
    {
        var selected = FolderWindowPicker.SelectedValue;
        var choices = draft?.Windows.Where(w => ApplicationRestoreAdapters.Detect(w).HasValue)
            .Select(w => new FolderWindowChoice(w.Id, w.Title ?? w.ProcessName ?? "Application")).ToArray() ?? [];
        FolderEditor.Visibility = choices.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        FolderWindowPicker.ItemsSource = choices;
        FolderWindowPicker.SelectedValue = selected is Guid id && choices.Any(c => c.Id == id) ? id : choices.FirstOrDefault()?.Id;
        UpdateFolderControls();
    }

    private SnapshotWindow? SelectedFolderWindow =>
        FolderWindowPicker.SelectedValue is Guid id && _draft is { } draft && draft.DesktopId == SelectedId
            ? draft.Windows.SingleOrDefault(w => w.Id == id) : null;

    private void OnFolderWindowSelected(object sender, SelectionChangedEventArgs e)
    {
        if (FolderContextText is not null && ChooseFolderButton is not null && ClearFolderButton is not null) UpdateFolderControls();
    }

    private void UpdateFolderControls()
    {
        var window = SelectedFolderWindow;
        FolderContextText.Text = window?.FolderContext?.Folder ?? "No folder assigned.";
        ChooseFolderButton.IsEnabled = !_busy && window is not null;
        ClearFolderButton.IsEnabled = !_busy && window?.FolderContext is not null;
    }

    private void OnChooseFolder(object sender, RoutedEventArgs e)
    {
        if (_busy || SelectedFolderWindow is not { } window
            || ApplicationRestoreAdapters.Detect(window) is not { } kind) return;
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose the folder to reopen" };
        if (window.FolderContext is { } previous) dialog.InitialDirectory = previous.Folder;
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var context = new ApplicationFolderContext(kind, dialog.FolderName);
        if (!ApplicationRestoreAdapters.IsValidContext(window, context)
            || ApplicationRestoreAdapters.CheckFolder(ApplicationRestoreAdapters.Build(window with { FolderContext = context })) is { })
        {
            StatusText.Text = "Choose an existing local folder supported by the application adapter.";
            return;
        }
        SetFolder(window.Id, context);
    }

    private void OnClearFolder(object sender, RoutedEventArgs e)
    {
        if (!_busy && SelectedFolderWindow is { } window) SetFolder(window.Id, null);
    }

    private void SetFolder(Guid windowId, ApplicationFolderContext? context)
    {
        if (_draft is not { } draft || draft.DesktopId != SelectedId) return;
        _draft = draft with { Windows = draft.Windows.Select(w => w.Id == windowId ? w with { FolderContext = context } : w).ToArray() };
        Render();
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
            InventoryExpander.IsExpanded = false;
            PlanCard.BringIntoView();
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
        _planChoices = [];
        LaunchMissingCheck.IsChecked = false;
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
        var priorSelection = _reviewedPlan is null ? RestoreSelection.Default(plan)
            : RestoreSelection.Retain(_reviewedPlan, plan, _planChoices.Where(w => w.Selected && w.Eligible).Select(w => w.Id).ToHashSet());
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
        _planChoices = plan.Windows.Select(w => new PlanWindowChoice(w.WindowId,
            byId[w.WindowId].Title ?? byId[w.WindowId].ProcessName ?? "Unknown application",
            $"{w.Kind} · {w.TargetSource ?? "No target"} · {w.State}{(w.RequiresReview ? " · Review required" : "")}"
            + (w.ScreenBounds is { } b ? $" · ({b.Left}, {b.Top}) · {b.Width} × {b.Height} px" : "")
            + Environment.NewLine + w.Reason, RestoreSelection.CanSelect(w),
            RestoreSelection.CanSelect(w) && priorSelection.Contains(w.WindowId))).ToArray();
        PlanRows.ItemsSource = _planChoices;
        PlanWarnings.Text = string.Join(Environment.NewLine, topology.Warnings.Append(
            "Preview reflects the displays at the time shown. Refresh after changing your monitor configuration. Running window identity and normal state will be verified before any move."));
        PlanCard.Visibility = Visibility.Visible;
        UpdateSelectionPreview();
    }

    private void OnPlanSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (_busy || sender is not System.Windows.Controls.CheckBox { DataContext: PlanWindowChoice choice } check) return;
        choice.Selected = choice.Eligible && check.IsChecked == true;
        UpdateSelectionPreview();
    }

    private void OnSelectSafe(object sender, RoutedEventArgs e) => SelectWindows(false, false);
    private void OnSelectAll(object sender, RoutedEventArgs e) => SelectWindows(true, false);
    private void OnSelectNone(object sender, RoutedEventArgs e) => SelectWindows(false, true);

    private void SelectWindows(bool includeReview, bool clear)
    {
        if (_busy || _reviewedPlan is not { } plan) return;
        var allowed = clear ? new HashSet<Guid>() : includeReview
            ? plan.Windows.Where(RestoreSelection.CanSelect).Select(w => w.WindowId).ToHashSet()
            : RestoreSelection.Default(plan).ToHashSet();
        foreach (var choice in _planChoices) choice.Selected = choice.Eligible && allowed.Contains(choice.Id);
        PlanRows.ItemsSource = null;
        PlanRows.ItemsSource = _planChoices;
        UpdateSelectionPreview();
    }

    private void UpdateSelectionPreview()
    {
        if (_busy || _reviewedPlan is not { } plan) return;
        var selected = _planChoices.Where(w => w.Eligible && w.Selected).Select(w => w.Id).ToHashSet();
        SelectionSummary.Text = $"{selected.Count} of {_planChoices.Count(w => w.Eligible)} eligible placements selected";
        DrawLayout(PlanCanvas, plan.Current.Monitors, plan.Windows.Reverse()
            .Where(w => selected.Contains(w.WindowId) && w.ScreenBounds.HasValue)
            .Select(w => w.ScreenBounds!.Value).ToArray());
        UpdateRestoreAvailability();
    }

    private void UpdateRestoreAvailability()
    {
        RestoreButton.IsEnabled = !_busy && _storageAvailable && _reviewedPlan is not null
            && SelectedId.HasValue && SelectedId == _active
            && _planChoices.Any(w => w.Eligible && w.Selected);
        RestoreButton.Content = $"Restore selected ({_planChoices.Count(w => w.Eligible && w.Selected)})...";
        LaunchMissingCheck.IsEnabled = RestoreButton.IsEnabled;
        RestoreHint.Text = SelectedId != _active || _active is null
            ? "Switch to the snapshot's original desktop before restoring."
            : "Only verified normal windows are positioned. Missing applications are launched only when explicitly enabled and approved.";
    }

    private void ClearResults()
    {
        _resultDesktop = null;
        RestoreResultsCard.Visibility = Visibility.Collapsed;
        RestoreRows.ItemsSource = null;
        RestoreProgress.Visibility = Visibility.Collapsed;
        RestoreProgress.Value = 0;
    }

    private void OnCancelRestore(object sender, RoutedEventArgs e)
    {
        _restoreCancellation?.Cancel();
        CancelRestoreButton.IsEnabled = false;
        RestoreSummary.Text = "Cancelling remaining operations...";
    }

    private async void OnRestore(object sender, RoutedEventArgs e)
    {
        if (_busy || !_storageAvailable || _reviewedPlan is not { } reviewed || SelectedSaved is not { } saved
            || SelectedId != _active || saved.DesktopId != reviewed.DesktopId || saved.CapturedUtc != reviewed.SnapshotCapturedUtc)
            return;
        var policy = FallbackPicker.SelectedIndex == 1 ? UnmatchedMonitorPolicy.Primary : UnmatchedMonitorPolicy.Skip;
        var launchMissing = LaunchMissingCheck.IsChecked == true;
        var selected = _planChoices.Where(w => w.Eligible && w.Selected).Select(w => w.Id).ToHashSet();
        if (selected.Count == 0) return;
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
        RestoreResultsCard.BringIntoView();
        var environment = new NativeExistingWindowRestoreEnvironment();
        var service = new ExistingWindowRestoreService(environment);
        var launches = new MissingApplicationLaunchService(environment, new NativeMissingApplicationLaunchEnvironment());
        try
        {
            VerifySavedSnapshot(saved);
            var approved = await RunOnSta(() => RestoreSelection.Apply(service.Prepare(saved, reviewed, policy, cancellation.Token), selected));
            if (_stopped) return;
            cancellation.Token.ThrowIfCancellationRequested();
            MissingApplicationLaunchPreparation? launchPlan = launchMissing
                ? await RunOnSta(() => launches.Prepare(approved, cancellation.Token)) : null;
            if (_stopped) return;
            cancellation.Token.ThrowIfCancellationRequested();
            var byId = launchPlan?.Items.ToDictionary(i => i.RestoreItem.WindowId);
            RestoreRows.ItemsSource = approved.Items.Select(i =>
                byId is not null && byId.TryGetValue(i.WindowId, out var launch)
                    ? new DetailRow(i.Label, $"{launch.Status} · {launch.ExecutablePath} · {launch.Detail}")
                    : new DetailRow(i.Label, $"{i.Status} · {i.Detail}")).ToArray();
            var ready = approved.Items.Where(i => i.Status == ExistingRestoreStatus.Ready).ToArray();
            var launchReady = launchPlan?.Items.Where(i => i.Status == ExistingRestoreStatus.LaunchReady).ToArray()
                ?? Array.Empty<MissingApplicationLaunchItem>();
            var unselected = approved.Items.Count(i => i.Status == ExistingRestoreStatus.UserSkipped);
            RestoreSummary.Text = $"{ready.Length} matched · {launchReady.Length} eligible launches · {unselected} not selected · {approved.Items.Count - ready.Length - launchReady.Length - unselected} other incomplete";
            if (ready.Length == 0 && launchReady.Length == 0) return;
            var launchNames = string.Join(Environment.NewLine, launchReady.Select(i => i.Spec?.Description ?? i.ExecutablePath));
            var names = string.Join(Environment.NewLine, ready.Take(8).Select(i => $"• {i.Label}"));
            if (ready.Length > 8) names += $"{Environment.NewLine}… and {ready.Length - 8} more";
            var launchDescription = launchReady.Length == 0 ? "No applications will be launched."
                : $"Launch these {launchReady.Length} approved applications and folder requests:{Environment.NewLine}{launchNames}{Environment.NewLine}Apps may activate themselves. Launches cannot be undone automatically.";
            if (!Confirm($"Reposition {ready.Length} matching running windows on this desktop?{Environment.NewLine}{names}{Environment.NewLine}{Environment.NewLine}{launchDescription}{Environment.NewLine}{Environment.NewLine}Geometry changes may be partial and are not automatically undone.", "Restore desktop"))
            {
                RestoreSummary.Text = "Restore cancelled. No placement or launch requests issued.";
                return;
            }

            if (_stopped) return;
            cancellation.Token.ThrowIfCancellationRequested();
            VerifySavedSnapshot(saved);
            RestoreSummary.Text = "Restoring selected windows...";
            var pendingLaunches = launchPlan?.Items.Select(i => i.RestoreItem.WindowId).ToHashSet() ?? [];
            var progressRows = new Dictionary<Guid, ExistingRestoreOutcome>();
            RestoreProgress.Maximum = Math.Max(1, approved.Items.Count);
            RestoreProgress.Value = 0;
            RestoreProgress.Visibility = Visibility.Visible;
            RestoreRows.ItemsSource = approved.Items.Select(i => new DetailRow(i.Label, "Pending...")).ToArray();
            var acceptingProgress = true;
            void Report(ExistingRestoreOutcome outcome)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_stopped || !acceptingProgress || _restoreCancellation != cancellation) return;
                    progressRows[outcome.WindowId] = outcome;
                    RestoreProgress.Value = progressRows.Count;
                    RestoreSummary.Text = cancellation.IsCancellationRequested ? "Cancelling remaining operations..."
                        : $"Processed {progressRows.Count} of {approved.Items.Count} windows";
                    RestoreRows.ItemsSource = approved.Items.Select(i => progressRows.TryGetValue(i.WindowId, out var completed)
                        ? new DetailRow(i.Label, $"{completed.Status} · {completed.Detail}")
                        : new DetailRow(i.Label, "Pending...")).ToArray();
                }));
            }
            var results = await RunOnSta(() =>
            {
                var existingResults = service.Execute(approved, cancellation.Token, outcome =>
                {
                    if (!pendingLaunches.Contains(outcome.WindowId)) Report(outcome);
                });
                return launchPlan is null ? existingResults : launches.Execute(launchPlan, existingResults, cancellation.Token, Report);
            });
            acceptingProgress = false;
            if (!_stopped) RestoreProgress.Value = results.Count;
            if (_stopped) return;
            RestoreRows.ItemsSource = results.Select(i => new DetailRow(i.Label, $"{i.Status} · {i.Detail}")).ToArray();
            RestoreSummary.Text = $"{results.Count(i => i.Status == ExistingRestoreStatus.Restored)} restored · "
                + $"{results.Count(i => i.Status == ExistingRestoreStatus.LaunchedAndRestored)} launched and restored · "
                + $"{results.Count(i => i.Status == ExistingRestoreStatus.AlreadyPlaced)} already placed · "
                + $"{results.Count(i => i.Status == ExistingRestoreStatus.UserSkipped)} not selected · "
                + $"{results.Count(i => i.Status is not (ExistingRestoreStatus.Restored or ExistingRestoreStatus.AlreadyPlaced or ExistingRestoreStatus.LaunchedAndRestored or ExistingRestoreStatus.UserSkipped))} other incomplete";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!_stopped) RestoreSummary.Text = "Cancelled before execution. No placement or launch requests issued.";
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

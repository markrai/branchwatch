using System.Windows;
using System.Windows.Threading;

namespace BranchWatch;

public sealed class StickyNotesController : IDisposable
{
    private readonly VirtualDesktopMonitor _monitor;
    private readonly StickyNoteStore _store;
    private readonly AppSettings _settings;
    private readonly IStickyNoteDesktopManager _native;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<Guid, StickyNoteWindow> _windows = [];
    private List<StickyNote> _notes = [];
    private bool _available;
    private bool _dirty;
    private bool _saveErrorReported;
    private bool _disposed;
    private Guid? _restoreDesktop;
    private int _restoreAttempts;

    internal StickyNotesController(VirtualDesktopMonitor monitor, string path, AppSettings? settings = null,
        IStickyNoteDesktopManager? native = null)
    {
        _monitor = monitor;
        _store = new StickyNoteStore(path);
        _settings = settings ?? new AppSettings();
        _native = native ?? new NativeDesktopManager();
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTimerTick;
    }

    internal event Action<string>? Error;

    internal IReadOnlyList<StickyNote> Notes => _notes;
    internal bool IsAvailable => _available && !_disposed;
    internal StickyNoteAppearance DefaultAppearance => StickyNoteAppearance.FromSettings(_settings);

    internal StickyNote CreateDefaultNote()
    {
        var note = new StickyNote();
        DefaultAppearance.ApplyTo(note);
        return note;
    }

    internal bool ApplyAppearanceToAll(StickyNoteAppearance appearance)
    {
        if (!IsAvailable)
        {
            return false;
        }

        foreach (var note in _notes)
        {
            if (_windows.TryGetValue(note.Id, out var window))
            {
                window.ApplyAppearance(appearance);
            }
            else
            {
                appearance.ApplyTo(note);
            }
        }

        MarkDirty();
        return Save();
    }
    internal IReadOnlyList<VirtualDesktopInfo> Desktops =>
        VirtualDesktopRegistryReader.TryGetDisplayState()?.AllDesktops
        ?? _monitor.CurrentState?.AllDesktops ?? Array.Empty<VirtualDesktopInfo>();

    internal bool IsNoteOnCurrentDesktop(StickyNoteWindow window) =>
        window.Handle != IntPtr.Zero && _native.IsCurrent(window.Handle);

    internal bool IsPassiveBodyOnOwnerDesktop(StickyNotePassiveBody body, StickyNoteWindow owner, out string reason)
    {
        var ownerDesktop = _native.GetDesktop(owner.Handle);
        var ownerCurrent = ownerDesktop.HasValue && _native.IsCurrent(owner.Handle);
        var bodyDesktop = _native.GetDesktop(body.Handle);
        var bodyCurrent = bodyDesktop.HasValue && _native.IsCurrent(body.Handle);
        return StickyNoteDesktopAffinity.Verify(body.HasNativeOwner, ownerDesktop, ownerCurrent,
            bodyDesktop, bodyCurrent, out reason);
    }

    internal void ReportClickThroughFailure(string? detail) =>
        Error?.Invoke($"Sticky note click-through unavailable. {detail ?? "Unknown window error."} The note remains visible.");

    internal string DesktopName(Guid id) =>
        Desktops.FirstOrDefault(desktop => desktop.Id == id)?.DisplayName ?? "Unavailable desktop";

    internal void Start()
    {
        if (_disposed || _available) return;
        try
        {
            _notes = _store.Load();
            _available = true;
        }
        catch (Exception ex)
        {
            Error?.Invoke($"Unable to load sticky notes. The existing file was preserved. {ex.Message}");
            return;
        }

        AdvanceAlarms(DateTimeOffset.UtcNow);
        Save();
        _monitor.StateChanged += OnDesktopChanged;
        _timer.Start();
        if (_monitor.CurrentState is { } state)
        {
            ScheduleRestore(state.ActiveDesktop.Id);
        }
    }

    internal void CreateNote()
    {
        if (!_available || _disposed)
        {
            Error?.Invoke("Sticky notes are unavailable. Check the saved notes file and restart BranchWatch.");
            return;
        }

        var work = SystemParameters.WorkArea;
        var offset = (_windows.Count % 8) * 22;
        var note = CreateDefaultNote();
        note.Left = work.Right - note.Width - 24 - offset;
        note.Top = work.Top + 24 + offset;

        if (!OpenWindow(note, null, activate: true))
        {
            return;
        }

        _notes.Add(note);
        MarkDirty();
        Save();
    }

    internal void OpenOnCurrentDesktop(StickyNote note)
    {
        if (!_available || _disposed)
        {
            return;
        }

        if (_windows.TryGetValue(note.Id, out var existing))
        {
            if (!_native.IsCurrent(existing.Handle))
            {
                var current = VirtualDesktopRegistryReader.TryGetCurrentDesktop();
                if (current is null || !MoveNote(note, current.Id, requireCurrent: true))
                {
                    Error?.Invoke("Unable to bring this note to the current virtual desktop.");
                    return;
                }
            }

            existing.FocusEditor();
            return;
        }

        if (OpenWindow(note, null, activate: true))
        {
            MarkDirty();
            Save();
        }
    }

    internal bool MoveNote(StickyNote note, Guid desktopId, bool requireCurrent = false)
    {
        if (!IsAvailable) return false;
        if (!_windows.TryGetValue(note.Id, out var window)
            || !Desktops.Any(desktop => desktop.Id == desktopId))
        {
            Error?.Invoke("The selected virtual desktop is unavailable.");
            return false;
        }

        var previous = _native.GetDesktop(window.Handle);
        if (previous is null || !_native.Move(window.Handle, desktopId))
        {
            Error?.Invoke("Windows could not move this note to the selected virtual desktop.");
            return false;
        }

        if (requireCurrent && !_native.IsCurrent(window.Handle))
        {
            _native.Move(window.Handle, previous.Value);
            SynchronizeAssignments();
            return false;
        }

        note.DesktopId = desktopId;
        window.RefreshDesktopName();
        window.RefreshPassiveBody();
        MarkDirty();
        Save();
        return true;
    }

    internal void DeleteNote(StickyNote note)
    {
        if (!IsAvailable) return;
        if (_windows.Remove(note.Id, out var window))
        {
            window.Close();
        }

        _notes.Remove(note);
        MarkDirty();
        Save();
    }

    internal void MarkDirty()
    {
        if (_available)
        {
            _dirty = true;
        }
    }

    private bool OpenWindow(StickyNote note, Guid? expectedDesktop, bool activate, bool reportError = true)
    {
        var window = new StickyNoteWindow(note, this) { ShowActivated = false };
        window.Show();

        var actualDesktop = _native.GetDesktop(window.Handle);
        if (actualDesktop is null || !_native.IsCurrent(window.Handle)
            || (expectedDesktop.HasValue && actualDesktop != expectedDesktop))
        {
            window.Close();
            if (reportError)
            {
                Error?.Invoke("Windows could not verify the note's virtual desktop. The note was not opened.");
            }

            return false;
        }

        note.DesktopId = actualDesktop.Value;
        note.IsOpen = true;
        _windows.Add(note.Id, window);
        window.RefreshDesktopName();
        window.BeginTrackingBounds();
        window.Closing += (_, _) =>
        {
            if (_windows.ContainsKey(note.Id))
            {
                if (_native.GetDesktop(window.Handle) is { } desktopId)
                {
                    note.DesktopId = desktopId;
                }

                window.CaptureBounds();
            }
        };
        window.Closed += (_, _) =>
        {
            if (_windows.Remove(note.Id))
            {
                note.IsOpen = false;
                MarkDirty();
                Save();
            }
        };

        if (activate)
        {
            window.FocusEditor();
        }

        return true;
    }

    private void ScheduleRestore(Guid desktopId)
    {
        _restoreDesktop = desktopId;
        _restoreAttempts = 3;
    }

    private void RestorePending()
    {
        if (!_available || _disposed || _restoreDesktop is not { } desktopId)
        {
            return;
        }

        var pending = StickyNote.ForDesktop(_notes, desktopId)
            .Where(note => !_windows.ContainsKey(note.Id)).ToArray();
        if (pending.Length == 0)
        {
            _restoreDesktop = null;
            return;
        }

        var failed = false;
        foreach (var note in pending)
        {
            if (!OpenWindow(note, desktopId, activate: false, reportError: false))
            {
                failed = true;
                break;
            }
        }

        if (!failed)
        {
            _restoreDesktop = null;
        }
        else if (--_restoreAttempts == 0)
        {
            _restoreDesktop = null;
            Error?.Invoke("Some sticky notes could not be restored. They remain available in the tray menu.");
        }
    }

    private void OnDesktopChanged(object? sender, VirtualDesktopDisplayState state)
    {
        if (!IsAvailable) return;
        SynchronizeAssignments();
        ScheduleRestore(state.ActiveDesktop.Id);
        foreach (var window in _windows.Values)
        {
            window.RefreshDesktopName();
            window.RefreshPassiveBody();
        }
    }

    private void SynchronizeAssignments()
    {
        foreach (var note in _notes)
        {
            if (_windows.TryGetValue(note.Id, out var window)
                && _native.GetDesktop(window.Handle) is { } desktopId && desktopId != note.DesktopId)
            {
                note.DesktopId = desktopId;
                window.RefreshDesktopName();
                MarkDirty();
            }
        }
    }

    internal void AlarmChanged()
    {
        MarkDirty();
        Save();
    }

    private void AdvanceAlarms(DateTimeOffset now)
    {
        foreach (var note in _notes)
            if (note.AdvanceAlarm(now)) MarkDirty();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (!IsAvailable) return;
        AdvanceAlarms(DateTimeOffset.UtcNow);
        SynchronizeAssignments();
        RestorePending();
        foreach (var window in _windows.Values)
        {
            window.RefreshAlarmPresentation();
            window.RefreshPassiveBody();
        }
        Save();
    }

    private bool Save()
    {
        if (!_available)
        {
            return false;
        }

        if (!_dirty)
        {
            return true;
        }

        try
        {
            _store.Save(_notes);
            _dirty = false;
            _saveErrorReported = false;
            return true;
        }
        catch (Exception ex)
        {
            if (!_saveErrorReported)
            {
                _saveErrorReported = true;
                Error?.Invoke($"Unable to save sticky notes. Changes will be retried. {ex.Message}");
            }

            return false;
        }
    }

    private void CapturePendingChanges()
    {
        SynchronizeAssignments();
        AdvanceAlarms(DateTimeOffset.UtcNow);
        foreach (var window in _windows.Values.ToArray())
        {
            window.FinishRename(commit: true, focusHeader: false);
            window.CaptureBounds();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _monitor.StateChanged -= OnDesktopChanged;
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        try
        {
            CapturePendingChanges();
            var windows = _windows.Values.ToArray();
            _windows.Clear();
            foreach (var window in windows)
            {
                window.Close();
            }
        }
        finally
        {
            try { Save(); }
            finally { _native.Dispose(); }
        }
    }
}

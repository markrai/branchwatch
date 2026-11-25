using System.Windows;
using System.Windows.Threading;

namespace BranchWatch;

public sealed class StickyNotesController : IDisposable
{
    private readonly VirtualDesktopMonitor _monitor;
    private readonly StickyNoteStore _store;
    private readonly NativeDesktopManager _native = new();
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<Guid, StickyNoteWindow> _windows = [];
    private List<StickyNote> _notes = [];
    private bool _available;
    private bool _dirty;
    private bool _saveErrorReported;
    private bool _disposed;

    internal StickyNotesController(VirtualDesktopMonitor monitor, string path)
    {
        _monitor = monitor;
        _store = new StickyNoteStore(path);
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTimerTick;
    }

    internal event Action<string>? Error;

    internal IReadOnlyList<StickyNote> Notes => _notes;
    internal IReadOnlyList<VirtualDesktopInfo> Desktops =>
        VirtualDesktopRegistryReader.TryGetDisplayState()?.AllDesktops
        ?? _monitor.CurrentState?.AllDesktops ?? Array.Empty<VirtualDesktopInfo>();

    internal string DesktopName(Guid id) =>
        Desktops.FirstOrDefault(desktop => desktop.Id == id)?.DisplayName ?? "Unavailable desktop";

    internal void Start()
    {
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

        _monitor.StateChanged += OnDesktopChanged;
        _timer.Start();
        if (_monitor.CurrentState is { } state)
        {
            Restore(state.ActiveDesktop.Id);
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
        var note = new StickyNote
        {
            Left = work.Right - 324 - offset,
            Top = work.Top + 24 + offset
        };

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
                if (current is null || !MoveNote(note, current.Id) || !_native.IsCurrent(existing.Handle))
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

    internal bool MoveNote(StickyNote note, Guid desktopId)
    {
        if (!_windows.TryGetValue(note.Id, out var window)
            || !Desktops.Any(desktop => desktop.Id == desktopId)
            || !_native.Move(window.Handle, desktopId))
        {
            Error?.Invoke("Windows could not move this note to the selected virtual desktop.");
            return false;
        }

        note.DesktopId = desktopId;
        window.RefreshDesktopName();
        MarkDirty();
        Save();
        return true;
    }

    internal void DeleteNote(StickyNote note)
    {
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

    private bool OpenWindow(StickyNote note, Guid? expectedDesktop, bool activate)
    {
        var window = new StickyNoteWindow(note, this) { ShowActivated = false };
        window.Show();

        var actualDesktop = _native.GetDesktop(window.Handle);
        if (actualDesktop is null || !_native.IsCurrent(window.Handle)
            || (expectedDesktop.HasValue && actualDesktop != expectedDesktop))
        {
            window.Close();
            Error?.Invoke("Windows could not verify the note's virtual desktop. The note was not opened.");
            return false;
        }

        note.DesktopId = actualDesktop.Value;
        note.IsOpen = true;
        _windows.Add(note.Id, window);
        window.RefreshDesktopName();
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

    private void Restore(Guid desktopId)
    {
        if (!_available || _disposed)
        {
            return;
        }

        foreach (var note in StickyNote.ForDesktop(_notes, desktopId))
        {
            if (!_windows.ContainsKey(note.Id))
            {
                OpenWindow(note, desktopId, activate: false);
            }
        }
    }

    private void OnDesktopChanged(object? sender, VirtualDesktopDisplayState state)
    {
        SynchronizeAssignments();
        Restore(state.ActiveDesktop.Id);
        foreach (var window in _windows.Values)
        {
            window.RefreshDesktopName();
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

    private void OnTimerTick(object? sender, EventArgs e)
    {
        SynchronizeAssignments();
        Save();
    }

    private void Save()
    {
        if (!_available || !_dirty)
        {
            return;
        }

        try
        {
            _store.Save(_notes);
            _dirty = false;
            _saveErrorReported = false;
        }
        catch (Exception ex)
        {
            if (!_saveErrorReported)
            {
                _saveErrorReported = true;
                Error?.Invoke($"Unable to save sticky notes. Changes will be retried. {ex.Message}");
            }
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
        SynchronizeAssignments();
        var windows = _windows.Values.ToArray();
        foreach (var window in windows)
        {
            window.CaptureBounds();
        }

        Save();
        _windows.Clear();
        foreach (var window in windows)
        {
            window.Close();
        }

        _native.Dispose();
    }
}

using System.Diagnostics;

namespace BranchWatch;

internal sealed class StickyNoteTemporaryEditing
{
    private enum Mode { Passive, Preview, Held }
    private Mode _mode;
    private long _started;
    internal static readonly TimeSpan Duration = TimeSpan.FromSeconds(5);

    internal bool IsActive => _mode != Mode.Passive;
    internal bool IsPreview => _mode == Mode.Preview;
    internal bool IsHeld => _mode == Mode.Held;

    internal void Begin(long timestamp)
    {
        _started = timestamp;
        _mode = Mode.Preview;
    }

    internal void BeginHeld() => _mode = Mode.Held;

    internal bool TryHold(long timestamp)
    {
        if (!IsPreview || Stopwatch.GetElapsedTime(_started, timestamp) >= Duration) return false;
        _mode = Mode.Held;
        return true;
    }

    internal bool End()
    {
        if (!IsActive) return false;
        _mode = Mode.Passive;
        return true;
    }
}

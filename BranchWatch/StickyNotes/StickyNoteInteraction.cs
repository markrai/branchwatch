namespace BranchWatch;

internal enum StickyNoteInteractionChange
{
    None,
    PreviewStarted,
    HeldStarted,
    PreviewHeld,
    EditingEnded,
    AlarmDismissed
}

internal enum StickyNoteRenameResult { None, Unchanged, Changed }

internal sealed class StickyNoteInteraction(StickyNote note)
{
    private readonly StickyNoteTemporaryEditing _editing = new();
    private string? _renameOriginal;

    internal bool IsClosing { get; private set; }
    internal bool IsAlarmDialogOpen { get; private set; }
    internal bool IsRenaming => _renameOriginal is not null;
    internal bool IsTemporaryEditing => _editing.IsActive;
    internal bool IsPreview => _editing.IsPreview;
    internal bool IsHeld => _editing.IsHeld;
    internal int EffectiveTransparencyLevel => IsTemporaryEditing ? StickyNoteTransparency.MinLevel : note.TransparencyLevel;
    internal bool WantsClickThrough => EffectiveTransparencyLevel > StickyNoteTransparency.MinLevel;
    internal bool CanBeginRename => !IsClosing && !IsRenaming && !IsAlarmDialogOpen;

    private bool CanTemporarilyEdit => CanBeginRename && !note.IsCollapsed
        && note.TransparencyLevel > StickyNoteTransparency.MinLevel;

    internal StickyNoteInteractionChange BeginEditorSession()
    {
        if (!CanTemporarilyEdit) return StickyNoteInteractionChange.None;
        _editing.BeginHeld();
        return StickyNoteInteractionChange.HeldStarted;
    }

    internal StickyNoteInteractionChange ToggleTemporaryEditing(long timestamp)
    {
        if (!CanTemporarilyEdit) return StickyNoteInteractionChange.None;
        if (IsTemporaryEditing) return EndEditing();
        _editing.Begin(timestamp);
        return StickyNoteInteractionChange.PreviewStarted;
    }

    internal StickyNoteInteractionChange HoldTemporaryEditing(long timestamp)
    {
        if (!IsPreview) return StickyNoteInteractionChange.None;
        return _editing.TryHold(timestamp) ? StickyNoteInteractionChange.PreviewHeld : EndEditing();
    }

    internal StickyNoteInteractionChange ExpirePreview() =>
        IsPreview ? EndEditing() : StickyNoteInteractionChange.None;

    internal StickyNoteInteractionChange Deactivate() =>
        IsHeld ? EndEditing() : StickyNoteInteractionChange.None;

    internal StickyNoteInteractionChange EndEditing() =>
        _editing.End() ? StickyNoteInteractionChange.EditingEnded : StickyNoteInteractionChange.None;

    internal StickyNoteInteractionChange HeaderClick(DateTimeOffset now, long timestamp)
    {
        if (IsClosing || IsAlarmDialogOpen) return StickyNoteInteractionChange.None;
        note.AdvanceAlarm(now);
        if (note.IsAlarmRinging)
        {
            note.ClearAlarm();
            return StickyNoteInteractionChange.AlarmDismissed;
        }

        return ToggleTemporaryEditing(timestamp);
    }

    internal bool TryBeginRename(string original)
    {
        if (!CanBeginRename || IsTemporaryEditing) return false;
        _renameOriginal = original;
        return true;
    }

    internal StickyNoteRenameResult FinishRename(string text, bool commit)
    {
        if (_renameOriginal is not { } original) return StickyNoteRenameResult.None;
        _renameOriginal = null;
        var previous = note.CustomTitle;
        if (commit && text != original) note.CustomTitle = text;
        return previous != note.CustomTitle ? StickyNoteRenameResult.Changed : StickyNoteRenameResult.Unchanged;
    }

    internal bool TryBeginAlarmDialog()
    {
        if (IsClosing || IsAlarmDialogOpen) return false;
        IsAlarmDialogOpen = true;
        return true;
    }

    internal void EndAlarmDialog() => IsAlarmDialogOpen = false;
    internal void BeginClosing() => IsClosing = true;
}

using System.Diagnostics;

namespace BranchWatch.Tests;

[TestClass]
public sealed class StickyNoteInteractionTests
{
    private static readonly long Start = 100 * Stopwatch.Frequency;
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 15, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void PreviewAndHeld_UseMonotonicDeadlineAndDistinctDeactivationPolicy()
    {
        var note = new StickyNote { TransparencyLevel = 8 };
        var interaction = new StickyNoteInteraction(note);
        Assert.IsTrue(interaction.WantsClickThrough);
        Assert.AreEqual(StickyNoteInteractionChange.PreviewStarted, interaction.ToggleTemporaryEditing(Start));
        Assert.IsTrue(interaction.IsPreview);
        Assert.IsFalse(interaction.WantsClickThrough);
        Assert.AreEqual(StickyNoteInteractionChange.None, interaction.Deactivate());
        Assert.AreEqual(StickyNoteInteractionChange.PreviewHeld,
            interaction.HoldTemporaryEditing(Start + 4 * Stopwatch.Frequency));
        Assert.IsTrue(interaction.IsHeld);
        Assert.AreEqual(StickyNoteInteractionChange.None, interaction.ExpirePreview());
        Assert.AreEqual(StickyNoteInteractionChange.EditingEnded, interaction.Deactivate());
        Assert.IsTrue(interaction.WantsClickThrough);
        Assert.AreEqual(8, note.TransparencyLevel);

        Assert.AreEqual(StickyNoteInteractionChange.PreviewStarted, interaction.ToggleTemporaryEditing(Start));
        Assert.AreEqual(StickyNoteInteractionChange.EditingEnded,
            interaction.HoldTemporaryEditing(Start + 5 * Stopwatch.Frequency));
        Assert.IsFalse(interaction.IsTemporaryEditing);
        Assert.AreEqual(StickyNoteInteractionChange.None, interaction.EndEditing());
    }

    [TestMethod]
    public void ExplicitEditorSession_IsHeldAndCanBeEndedOrToggledWithoutSavingOpacity()
    {
        var note = new StickyNote { TransparencyLevel = 10 };
        var interaction = new StickyNoteInteraction(note);
        Assert.AreEqual(StickyNoteInteractionChange.HeldStarted, interaction.BeginEditorSession());
        Assert.IsTrue(interaction.IsHeld);
        Assert.AreEqual(StickyNoteTransparency.MinLevel, interaction.EffectiveTransparencyLevel);
        Assert.AreEqual(StickyNoteInteractionChange.None, interaction.ExpirePreview());
        Assert.AreEqual(StickyNoteInteractionChange.EditingEnded, interaction.ToggleTemporaryEditing(Start));
        Assert.AreEqual(10, interaction.EffectiveTransparencyLevel);
        Assert.AreEqual(10, note.TransparencyLevel);

        note.TransparencyLevel = 1;
        Assert.AreEqual(StickyNoteInteractionChange.None, interaction.BeginEditorSession());
        Assert.AreEqual(StickyNoteInteractionChange.None, interaction.ToggleTemporaryEditing(Start));
        Assert.IsFalse(interaction.WantsClickThrough);

        note.TransparencyLevel = 8;
        note.IsCollapsed = true;
        Assert.AreEqual(StickyNoteInteractionChange.None, interaction.BeginEditorSession());
        Assert.AreEqual(StickyNoteInteractionChange.None, interaction.ToggleTemporaryEditing(Start));
        Assert.IsTrue(interaction.WantsClickThrough);
    }

    [TestMethod]
    public void HeaderClick_AcknowledgesDueAlarmBeforeAnyEditingTransition()
    {
        var note = new StickyNote { TransparencyLevel = 8 };
        var interaction = new StickyNoteInteraction(note);
        Assert.IsTrue(note.SetAlarm(Now.AddMinutes(1), Now));
        Assert.AreEqual(StickyNoteInteractionChange.PreviewStarted, interaction.HeaderClick(Now, Start));
        Assert.IsNotNull(note.AlarmDueUtc);
        Assert.AreEqual(StickyNoteInteractionChange.PreviewHeld, interaction.HoldTemporaryEditing(Start));
        Assert.AreEqual(StickyNoteInteractionChange.AlarmDismissed, interaction.HeaderClick(Now.AddMinutes(1), Start));
        Assert.IsTrue(interaction.IsHeld);
        Assert.IsNull(note.AlarmDueUtc);
        Assert.IsFalse(note.AlarmTriggered);
        Assert.AreEqual(StickyNoteInteractionChange.EditingEnded, interaction.HeaderClick(Now.AddMinutes(2), Start));

        note.IsCollapsed = true;
        Assert.IsTrue(note.SetAlarm(Now.AddMinutes(3), Now));
        Assert.AreEqual(StickyNoteInteractionChange.AlarmDismissed, interaction.HeaderClick(Now.AddMinutes(3), Start));
        Assert.IsFalse(interaction.IsTemporaryEditing);
        Assert.IsNull(note.AlarmDueUtc);
        Assert.AreEqual(StickyNoteInteractionChange.None, interaction.HeaderClick(Now.AddDays(1), Start));
    }

    [TestMethod]
    public void Rename_OwnsOriginalTextAndBlocksNewEditingUntilFinished()
    {
        var note = new StickyNote { TransparencyLevel = 8, Text = "Body" };
        var interaction = new StickyNoteInteraction(note);
        Assert.AreEqual(StickyNoteInteractionChange.HeldStarted, interaction.BeginEditorSession());
        Assert.IsFalse(interaction.TryBeginRename("Desktop"));
        Assert.AreEqual(StickyNoteInteractionChange.EditingEnded, interaction.EndEditing());
        Assert.IsTrue(interaction.TryBeginRename("Desktop"));
        Assert.IsFalse(interaction.TryBeginRename("Other"));
        Assert.AreEqual(StickyNoteInteractionChange.None, interaction.BeginEditorSession());
        Assert.AreEqual(StickyNoteInteractionChange.None, interaction.ToggleTemporaryEditing(Start));
        Assert.AreEqual(StickyNoteRenameResult.Unchanged, interaction.FinishRename("Desktop", true));
        Assert.IsNull(note.CustomTitle);
        Assert.IsTrue(interaction.TryBeginRename("Desktop"));
        Assert.AreEqual(StickyNoteRenameResult.Changed, interaction.FinishRename("  Project  ", true));
        Assert.AreEqual("Project", note.CustomTitle);
        Assert.IsTrue(interaction.TryBeginRename("Project"));
        Assert.AreEqual(StickyNoteRenameResult.Unchanged, interaction.FinishRename("Discard", false));
        Assert.AreEqual("Project", note.CustomTitle);
        Assert.IsTrue(interaction.TryBeginRename("Project"));
        Assert.AreEqual(StickyNoteRenameResult.Changed, interaction.FinishRename("   ", true));
        Assert.IsNull(note.CustomTitle);
        Assert.AreEqual(StickyNoteRenameResult.None, interaction.FinishRename("Ignored", true));
        Assert.AreEqual("Body", note.Text);
        Assert.IsFalse(interaction.IsRenaming);
    }

    [TestMethod]
    public void DialogGuard_PreventsReentryAndNewTemporaryEditingWithoutClearingAlarm()
    {
        var note = new StickyNote { TransparencyLevel = 8 };
        var interaction = new StickyNoteInteraction(note);
        Assert.IsTrue(note.SetAlarm(Now.AddMinutes(1), Now));
        Assert.AreEqual(StickyNoteInteractionChange.PreviewStarted, interaction.ToggleTemporaryEditing(Start));
        Assert.IsTrue(interaction.TryBeginAlarmDialog());
        Assert.IsFalse(interaction.TryBeginAlarmDialog());
        Assert.IsFalse(interaction.TryBeginRename("Desktop"));
        Assert.AreEqual(StickyNoteInteractionChange.None, interaction.HoldTemporaryEditing(Start));
        Assert.AreEqual(StickyNoteInteractionChange.EditingEnded, interaction.EndEditing());
        Assert.AreEqual(StickyNoteInteractionChange.None, interaction.BeginEditorSession());
        Assert.AreEqual(StickyNoteInteractionChange.None, interaction.HeaderClick(Now.AddMinutes(1), Start));
        Assert.IsNotNull(note.AlarmDueUtc);
        interaction.EndAlarmDialog();
        Assert.AreEqual(StickyNoteInteractionChange.AlarmDismissed, interaction.HeaderClick(Now.AddMinutes(1), Start));
        Assert.IsTrue(interaction.TryBeginAlarmDialog());
        interaction.EndAlarmDialog();
    }

    [TestMethod]
    public void Closing_RejectsNewActionsButAllowsPendingRenameToCommit()
    {
        var note = new StickyNote { TransparencyLevel = 8 };
        var interaction = new StickyNoteInteraction(note);
        Assert.IsTrue(interaction.TryBeginRename("Desktop"));
        interaction.BeginClosing();
        Assert.IsTrue(interaction.IsClosing);
        Assert.IsFalse(interaction.CanBeginRename);
        Assert.IsFalse(interaction.TryBeginAlarmDialog());
        Assert.AreEqual(StickyNoteInteractionChange.None, interaction.BeginEditorSession());
        Assert.AreEqual(StickyNoteInteractionChange.None, interaction.HeaderClick(Now, Start));
        Assert.AreEqual(StickyNoteRenameResult.Changed, interaction.FinishRename("Saved title", true));
        Assert.AreEqual("Saved title", note.CustomTitle);
        Assert.IsFalse(interaction.IsRenaming);
        Assert.IsFalse(interaction.TryBeginRename("Saved title"));
    }
}

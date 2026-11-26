using System.Drawing;
using Forms = System.Windows.Forms;

namespace BranchWatch.Tests;

[TestClass]
[DoNotParallelize]
public sealed class TrayMenuTests
{
    [TestMethod]
    public void Projection_PinnedAndEmptyRepositoryHaveCorrectCommandsAndLabels()
    {
        var settings = new AppSettings();
        var empty = TrayMenuState.From(settings, RepositoryStatus.Empty, null, null);
        Assert.AreEqual("Mode: Pinned Repo Mode", empty.Mode);
        Assert.AreEqual("Pinned repo: (none)", empty.Repository);
        Assert.AreEqual("Repository: (none)", empty.ActiveRepository);
        Assert.AreEqual("Branch: No repository selected", empty.Branch);
        Assert.AreEqual("BranchWatch", empty.NotifyText);
        Assert.IsTrue(empty.PinnedMode);
        Assert.IsFalse(empty.WorkspaceMode);
        Assert.IsFalse(empty.CanRefresh);
        Assert.IsFalse(empty.CanRescan);
        Assert.IsFalse(empty.ShowActivity);
        Assert.IsNull(empty.Status);

        var status = RepositoryStatus.Empty with { RepositoryRoot = @"C:\Projects\A&B", BranchDisplay = "main" };
        settings.OverlayVisible = false;
        settings.VirtualDesktopOverlayVisible = false;
        var pinned = TrayMenuState.From(settings, status, WorkspaceActivityReason.FileChanged, null);
        Assert.AreEqual(@"Pinned repo: C:\Projects\A&B", pinned.Repository);
        Assert.AreEqual("Repository: A&B", pinned.ActiveRepository);
        Assert.AreEqual(@"C:\Projects\A&B", pinned.RepositoryTooltip);
        Assert.AreEqual("BranchWatch - main", pinned.NotifyText);
        Assert.AreEqual("Show overlay", pinned.OverlayToggle);
        Assert.AreEqual("Show overlay", pinned.DesktopOverlayToggle);
        Assert.IsTrue(pinned.CanRefresh);
        Assert.IsFalse(pinned.ShowActivity);
        Assert.IsFalse(pinned.CanRescan);
    }

    [TestMethod]
    public void Projection_WorkspaceStatusPrecedenceAndTruncationPreserveTooltips()
    {
        var settings = new AppSettings { WatchMode = RepositoryWatchMode.WorkspaceRepo, WorkspaceRootPath = @"C:\Projects" };
        var longBranch = new string('b', 100);
        var longError = new string('e', 100);
        var status = RepositoryStatus.Empty with { RepositoryRoot = @"C:\Projects\Repo", BranchDisplay = longBranch, ErrorMessage = longError };
        var state = TrayMenuState.From(settings, status, WorkspaceActivityReason.IndexChanged, "Workspace status");
        Assert.AreEqual("Mode: Workspace Mode", state.Mode);
        Assert.AreEqual(@"Workspace: C:\Projects", state.Repository);
        Assert.AreEqual("Repository: Repo", state.ActiveRepository);
        Assert.AreEqual("Branch: " + new string('b', 42) + "...", state.Branch);
        Assert.AreEqual(longBranch, state.BranchTooltip);
        Assert.AreEqual("Status: " + new string('e', 45) + "...", state.Status);
        Assert.AreEqual(longError, state.StatusTooltip);
        Assert.AreEqual(63, state.NotifyText.Length);
        Assert.IsTrue(state.CanRescan);
        Assert.IsTrue(state.WorkspaceMode);
        Assert.IsFalse(state.PinnedMode);
        Assert.IsTrue(state.ShowActivity);
        Assert.AreEqual("Last activity: index changed", state.Activity);
        state = TrayMenuState.From(settings, status with { ErrorMessage = null }, null, "Workspace status");
        Assert.AreEqual("Status: Workspace status", state.Status);
        Assert.IsFalse(state.ShowActivity);
        state = TrayMenuState.From(settings, status with { ErrorMessage = null }, null, " ");
        Assert.IsNull(state.Status);
        Assert.IsNull(state.StatusTooltip);
    }

    [TestMethod]
    public void NoteProjection_PreservesOrderIdentityTitleAlarmAndClosedState()
    {
        var desktop = Guid.NewGuid();
        var a = new StickyNote { DesktopId = desktop, CustomTitle = "R&D", IsOpen = false,
            AlarmDueUtc = DateTimeOffset.UtcNow, AlarmTriggered = true };
        var b = new StickyNote { DesktopId = desktop, Text = "Body text", AlarmDueUtc = DateTimeOffset.UtcNow.AddDays(1) };
        var c = new StickyNote { DesktopId = Guid.NewGuid() };
        var rows = TrayMenuState.Notes([a, b, c], id => id == desktop ? "Work" : "Unavailable desktop");
        Assert.HasCount(3, rows);
        Assert.AreEqual(new TrayNoteChoice(a.Id, "R&D · Work (alarm due) (closed)"), rows[0]);
        Assert.AreEqual(new TrayNoteChoice(b.Id, "Body text · Work (alarm set)"), rows[1]);
        Assert.AreEqual(new TrayNoteChoice(c.Id, "Untitled note · Unavailable desktop"), rows[2]);
        Assert.AreEqual("R&&D · Work (alarm due) (closed)", TrayMenuState.Literal(rows[0].Label));
        a.CustomTitle = "Changed";
        Assert.AreEqual("R&D · Work (alarm due) (closed)", rows[0].Label);
    }

    [TestMethod]
    public void OverlayVisibility_SaveSuccessAndFailureHaveSharedRollbackSemantics()
    {
        var settings = new AppSettings();
        bool? saved = null;
        GitOverlayVisibility.Set(settings, false, value => saved = value.OverlayVisible);
        Assert.IsFalse(settings.OverlayVisible);
        Assert.IsFalse(saved ?? true);
        Assert.ThrowsExactly<InvalidOperationException>(() => GitOverlayVisibility.Set(settings, true,
            _ => throw new InvalidOperationException("Save failed")));
        Assert.IsFalse(settings.OverlayVisible);
        GitOverlayVisibility.Set(settings, true, value => saved = value.OverlayVisible);
        Assert.IsTrue(settings.OverlayVisible);
        Assert.IsTrue(saved ?? false);
    }

    [TestMethod]
    public void Menu_CommandsAndDynamicNoteRowsRetainTheirIdentity()
    {
        StaTest.Run(() =>
        {
            var settings = new AppSettings();
            var status = RepositoryStatus.Empty with { RepositoryRoot = @"C:\Projects\A&B", BranchDisplay = "feature/R&D" };
            var commands = new List<TrayCommand>();
            var opened = new List<Guid>();
            var first = new TrayNoteChoice(Guid.NewGuid(), "A&B");
            var second = new TrayNoteChoice(Guid.NewGuid(), "Second");
            TrayNoteChoice[] notes = [first];
            using var icon = (Icon)SystemIcons.Application.Clone();
            using var menu = new TrayMenu(icon, () => TrayMenuState.From(settings, status, null, null),
                () => notes, commands.Add, opened.Add);
            var top = menu.Menu.Items.OfType<Forms.ToolStripMenuItem>().ToArray();
            CollectionAssert.AreEqual(new[] { "BranchWatch", "Mode: Pinned Repo Mode", "Repository: A&&B",
                "Branch: feature/R&&D", "", "Git Watcher", "Virtual Desktops", "Sticky Notes",
                "Settings...", "About BranchWatch", "Exit" }, top.Select(i => i.Text).ToArray());
            var git = top.Single(i => i.Text == "Git Watcher");
            ((Forms.ToolStripMenuItem)git.DropDownItems[0]).PerformClick();
            ((Forms.ToolStripMenuItem)git.DropDownItems[1]).PerformClick();
            var desktops = top.Single(i => i.Text == "Virtual Desktops");
            ((Forms.ToolStripMenuItem)desktops.DropDownItems[1]).PerformClick();
            top.Single(i => i.Text == "Settings...").PerformClick();
            var sticky = top.Single(i => i.Text == "Sticky Notes");
            ((Forms.ToolStripMenuItem)sticky.DropDownItems[0]).PerformClick();
            CollectionAssert.AreEqual(new[] { TrayCommand.ToggleOverlay, TrayCommand.RefreshBranch,
                TrayCommand.MemorySettings, TrayCommand.GeneralSettings, TrayCommand.NewNote }, commands.ToArray());
            var reopen = (Forms.ToolStripMenuItem)sticky.DropDownItems[1];
            menu.RefreshNotes();
            var old = (Forms.ToolStripMenuItem)reopen.DropDownItems[0];
            Assert.AreEqual("A&&B", old.Text);
            old.PerformClick();
            notes = [second];
            menu.RefreshNotes();
            Assert.HasCount(1, reopen.DropDownItems);
            Assert.IsTrue(old.IsDisposed);
            ((Forms.ToolStripMenuItem)reopen.DropDownItems[0]).PerformClick();
            CollectionAssert.AreEqual(new[] { first.Id, second.Id }, opened.ToArray());
            notes = [];
            menu.RefreshNotes();
            Assert.IsEmpty(reopen.DropDownItems);
            Assert.IsFalse(reopen.Enabled);
            menu.Dispose();
            menu.RefreshNotes();
            menu.Apply(TrayMenuState.From(settings, status, null, null));
        });
    }
}

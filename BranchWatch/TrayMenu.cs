using System.Drawing;
using Forms = System.Windows.Forms;

namespace BranchWatch;

internal enum TrayCommand
{
    ToggleOverlay, RefreshBranch, RescanWorkspace, ChooseRepository, ChooseWorkspace,
    PinnedMode, WorkspaceMode, GitSettings, ToggleDesktopOverlay, MemorySettings,
    DesktopSettings, GeneralSettings, About, Exit, NewNote, NoteSettings
}

internal sealed class TrayMenu : IDisposable
{
    private readonly List<Image> _menuImages = [];
    private readonly Func<TrayNoteChoice[]> _notes;
    private readonly Action<TrayCommand> _dispatch;
    private readonly Action<Guid> _reopenNote;
    private readonly Forms.ToolStripMenuItem _desktopOverlayToggleItem;
    private readonly Forms.ToolStripMenuItem _modeItem;
    private readonly Forms.ToolStripMenuItem _repositoryItem;
    private readonly Forms.ToolStripMenuItem _activeRepositoryItem;
    private readonly Forms.ToolStripMenuItem _branchItem;
    private readonly Forms.ToolStripMenuItem _lastActivityItem;
    private readonly Forms.ToolStripMenuItem _statusItem;
    private readonly Forms.ToolStripMenuItem _rescanWorkspaceItem;
    private readonly Forms.ToolStripMenuItem _refreshItem;
    private readonly Forms.ToolStripMenuItem _overlayToggleItem;
    private readonly Forms.ToolStripMenuItem _pinnedRepoModeItem;
    private readonly Forms.ToolStripMenuItem _workspaceRepoModeItem;
    private bool _disposed;
    internal Forms.ContextMenuStrip Menu { get; }

    internal TrayMenu(Icon trayIcon, Func<TrayMenuState> state, Func<TrayNoteChoice[]> notes,
        Action<TrayCommand> dispatch, Action<Guid> reopenNote)
    {
        _notes = notes;
        _dispatch = dispatch;
        _reopenNote = reopenNote;
        Menu = new Forms.ContextMenuStrip();
        var menu = Menu;

        _modeItem = CreateDisabledItem("Mode: Pinned Repo Mode");
        _repositoryItem = CreateDisabledItem("Pinned repo: (none)");
        _activeRepositoryItem = CreateDisabledItem("Active repo: (none)");
        _activeRepositoryItem.Visible = false;
        _branchItem = CreateDisabledItem("Branch: No repository selected");
        _lastActivityItem = CreateDisabledItem("Last activity: (none)");
        _lastActivityItem.Visible = false;
        _statusItem = CreateDisabledItem(string.Empty);
        _statusItem.Visible = false;

        var title = CreateDisabledItem("BranchWatch");
        title.Image = trayIcon.ToBitmap();
        _menuImages.Add(title.Image);
        menu.Items.Add(title);
        menu.Items.Add(_modeItem);
        menu.Items.Add(_activeRepositoryItem);
        menu.Items.Add(_branchItem);
        menu.Items.Add(_statusItem);
        menu.Items.Add(new Forms.ToolStripSeparator());

        var git = new Forms.ToolStripMenuItem("Git Watcher") { Image = MenuIcon(TrayMenuGlyph.Git) };
        _overlayToggleItem = CreateItem("Hide overlay", (_, _) => _dispatch(TrayCommand.ToggleOverlay));
        _refreshItem = CreateItem("Refresh branch", (_, _) => _dispatch(TrayCommand.RefreshBranch));
        _rescanWorkspaceItem = CreateItem("Rescan workspace", (_, _) => _dispatch(TrayCommand.RescanWorkspace));
        git.DropDownItems.AddRange([_overlayToggleItem, _refreshItem, _rescanWorkspaceItem,
            new Forms.ToolStripSeparator(),
            CreateItem("Choose repository...", (_, _) => _dispatch(TrayCommand.ChooseRepository)),
            CreateItem("Choose workspace...", (_, _) => _dispatch(TrayCommand.ChooseWorkspace))]);
        var mode = new Forms.ToolStripMenuItem("Watch mode");
        _pinnedRepoModeItem = CreateItem("Pinned repository", (_, _) => _dispatch(TrayCommand.PinnedMode));
        _workspaceRepoModeItem = CreateItem("Workspace", (_, _) => _dispatch(TrayCommand.WorkspaceMode));
        mode.DropDownItems.AddRange([_pinnedRepoModeItem, _workspaceRepoModeItem]);
        git.DropDownItems.Add(mode);
        git.DropDownItems.Add(_repositoryItem);
        git.DropDownItems.Add(_lastActivityItem);
        git.DropDownItems.Add(new Forms.ToolStripSeparator());
        git.DropDownItems.Add(CreateItem("Preferences...", (_, _) => _dispatch(TrayCommand.GitSettings)));

        var desktops = new Forms.ToolStripMenuItem("Virtual Desktops") { Image = MenuIcon(TrayMenuGlyph.Desktops) };
        _desktopOverlayToggleItem = CreateItem("Hide overlay", (_, _) => _dispatch(TrayCommand.ToggleDesktopOverlay));
        desktops.DropDownItems.Add(_desktopOverlayToggleItem);
        desktops.DropDownItems.Add(CreateItem("Desktop Memory...", (_, _) => _dispatch(TrayCommand.MemorySettings)));
        desktops.DropDownItems.Add(CreateItem("Preferences...", (_, _) => _dispatch(TrayCommand.DesktopSettings)));

        var settings = CreateItem("Settings...", (_, _) => _dispatch(TrayCommand.GeneralSettings));
        settings.Image = MenuIcon(TrayMenuGlyph.Settings);
        var about = CreateItem("About BranchWatch", (_, _) => _dispatch(TrayCommand.About));
        about.Image = MenuIcon(TrayMenuGlyph.About);
        var exit = CreateItem("Exit", (_, _) => _dispatch(TrayCommand.Exit));
        exit.Image = MenuIcon(TrayMenuGlyph.Exit);
        menu.Items.AddRange([git, desktops, BuildStickyNotesMenu(), new Forms.ToolStripSeparator(),
            settings, about, new Forms.ToolStripSeparator(), exit]);

        menu.Opening += (_, _) => Apply(state());
        Apply(state());
    }

    private Forms.ToolStripMenuItem BuildStickyNotesMenu()
    {
        var menu = new Forms.ToolStripMenuItem("Sticky Notes") { Image = MenuIcon(TrayMenuGlyph.Notes) };
        menu.DropDownItems.Add(CreateItem("New note", (_, _) => _dispatch(TrayCommand.NewNote)));
        var reopen = new Forms.ToolStripMenuItem("Open on this desktop");
        menu.DropDownItems.Add(reopen);
        menu.DropDownItems.Add(new Forms.ToolStripSeparator());
        menu.DropDownItems.Add(CreateItem("Preferences...", (_, _) => _dispatch(TrayCommand.NoteSettings)));
        menu.DropDownOpening += (_, _) =>
        {
            while (reopen.DropDownItems.Count > 0)
            {
                var item = reopen.DropDownItems[0];
                reopen.DropDownItems.RemoveAt(0);
                item.Dispose();
            }

            foreach (var note in _notes())
                reopen.DropDownItems.Add(CreateItem(TrayMenuState.Literal(note.Label), (_, _) => _reopenNote(note.Id)));

            reopen.Enabled = reopen.DropDownItems.Count > 0;
        };
        return menu;
    }


    internal void Apply(TrayMenuState state)
    {
        if (_disposed) return;
        _modeItem.Text = state.Mode;
        _repositoryItem.Text = TrayMenuState.Literal(state.Repository);
        _activeRepositoryItem.Text = TrayMenuState.Literal(state.ActiveRepository);
        _activeRepositoryItem.ToolTipText = state.RepositoryTooltip;
        _activeRepositoryItem.Visible = true;
        _branchItem.Text = TrayMenuState.Literal(state.Branch);
        _branchItem.ToolTipText = state.BranchTooltip;
        _lastActivityItem.Text = state.Activity;
        _lastActivityItem.Visible = state.ShowActivity;
        _statusItem.Text = TrayMenuState.Literal(state.Status ?? "");
        _statusItem.ToolTipText = state.StatusTooltip ?? "";
        _statusItem.Visible = state.Status is not null;
        _rescanWorkspaceItem.Enabled = state.CanRescan;
        _refreshItem.Enabled = state.CanRefresh;
        _overlayToggleItem.Text = state.OverlayToggle;
        _pinnedRepoModeItem.Checked = state.PinnedMode;
        _workspaceRepoModeItem.Checked = state.WorkspaceMode;
        _desktopOverlayToggleItem.Text = state.DesktopOverlayToggle;
    }

    private Image MenuIcon(TrayMenuGlyph glyph)
    {
        var image = TrayMenuIcons.Create(glyph);
        _menuImages.Add(image);
        return image;
    }

    private static Forms.ToolStripMenuItem CreateDisabledItem(string text)
    {
        return new Forms.ToolStripMenuItem(text)
        {
            Enabled = false
        };
    }

    private static Forms.ToolStripMenuItem CreateItem(string text, EventHandler onClick)
    {
        var item = new Forms.ToolStripMenuItem(text);
        item.Click += onClick;
        return item;
    }


    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Menu.Dispose();
        foreach (var image in _menuImages) image.Dispose();
        _menuImages.Clear();
    }
}

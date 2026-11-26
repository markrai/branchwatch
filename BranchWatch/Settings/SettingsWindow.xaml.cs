using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using WpfButton = System.Windows.Controls.Button;
using WpfBorder = System.Windows.Controls.Border;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfListBoxItem = System.Windows.Controls.ListBoxItem;

namespace BranchWatch;

internal enum SettingsSection
{
    General,
    GitWatcher,
    VirtualDesktops,
    DesktopMemory,
    StickyNotes,
    About
}

public partial class SettingsWindow : Window
{
    private readonly SettingsService _service;
    private readonly AppSettings _settings;
    private readonly VirtualDesktopOverlayController _desktops;
    private readonly Action _applyGit;
    private readonly Action _chooseRepository;
    private readonly Action _chooseWorkspace;
    private readonly Action _pinnedMode;
    private readonly Action _workspaceMode;
    private readonly Action _toggleStartup;
    private readonly StickyNotesController _stickyNotes;
    private bool _ready;

    internal SettingsWindow(SettingsService service, AppSettings settings, StickyNotesController stickyNotes,
        VirtualDesktopOverlayController desktops, Action applyGit, Action chooseRepository,
        Action chooseWorkspace, Action pinnedMode, Action workspaceMode, Action toggleStartup)
    {
        _service = service;
        _settings = settings;
        _desktops = desktops;
        _stickyNotes = stickyNotes;
        _applyGit = applyGit;
        _chooseRepository = chooseRepository;
        _chooseWorkspace = chooseWorkspace;
        _pinnedMode = pinnedMode;
        _workspaceMode = workspaceMode;
        _toggleStartup = toggleStartup;

        InitializeComponent();
        var work = SystemParameters.WorkArea;
        MaxWidth = Math.Max(MinWidth, work.Width * 0.96);
        MaxHeight = Math.Max(MinHeight, work.Height * 0.94);
        var version = typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3) ?? "Unknown";
        SidebarVersion.Text = $"Version {version}";
        AboutVersion.Text = $"Version {version}";
        StoragePath.Text = Path.GetDirectoryName(service.SettingsPath) ?? service.SettingsPath;

        _desktops.Monitor.StateChanged += OnDesktopStateChanged;
        Closed += (_, _) =>
        {
            _ready = false;
            _desktops.Monitor.StateChanged -= OnDesktopStateChanged;
            if (MemoryHost.Content is DesktopMemorySettingsPage memory) memory.Stop();
            MemoryHost.Content = null;
            GitOverlayHost.Content = null;
            DesktopOverlayHost.Content = null;
            NotesHost.Content = null;
            DesktopColorRows.Children.Clear();
        };
        _ready = true;
        Navigate(SettingsSection.General);
    }

    internal void Navigate(SettingsSection section)
    {
        if (!_ready) return;
        if (Navigation.SelectedIndex == (int)section)
        {
            ShowSection(section);
            return;
        }

        Navigation.SelectedIndex = (int)section;
    }

    private void OnNavigationChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_ready && Navigation.SelectedItem is WpfListBoxItem { Tag: string tag }
            && Enum.TryParse<SettingsSection>(tag, out var section))
        {
            ShowSection(section);
        }
    }

    private void ShowSection(SettingsSection section)
    {
        EnsureSectionContent(section);
        GeneralPage.Visibility = section == SettingsSection.General ? Visibility.Visible : Visibility.Collapsed;
        GitPage.Visibility = section == SettingsSection.GitWatcher ? Visibility.Visible : Visibility.Collapsed;
        DesktopsPage.Visibility = section == SettingsSection.VirtualDesktops ? Visibility.Visible : Visibility.Collapsed;
        MemoryPage.Visibility = section == SettingsSection.DesktopMemory ? Visibility.Visible : Visibility.Collapsed;
        NotesPage.Visibility = section == SettingsSection.StickyNotes ? Visibility.Visible : Visibility.Collapsed;
        AboutPage.Visibility = section == SettingsSection.About ? Visibility.Visible : Visibility.Collapsed;
        (SectionTitle.Text, SectionDescription.Text) = section switch
        {
            SettingsSection.GitWatcher => ("Git Watcher", "Choose what to monitor and customize the repository overlay."),
            SettingsSection.VirtualDesktops => ("Virtual Desktops", "Customize the desktop labels and their placement."),
            SettingsSection.DesktopMemory => ("Desktop Memory", "Capture and inspect saved working environments."),
            SettingsSection.StickyNotes => ("Sticky Notes", "Set the appearance of new notes or update existing notes."),
            SettingsSection.About => ("About", "Application information and project links."),
            _ => ("General", "Manage startup and local application data.")
        };
        FooterText.Text = section == SettingsSection.StickyNotes
            ? "Use Save defaults or Apply to all notes to save appearance changes."
            : section == SettingsSection.DesktopMemory ? "Snapshots are stored locally only when you choose Save snapshot." : "Changes are saved automatically.";
        RefreshRepositoryState();
        StartupCheck.IsChecked = _settings.StartWithWindows;
        if (section == SettingsSection.VirtualDesktops && DesktopOverlayHost.Content is DesktopOverlaySettingsPage desktop)
        {
            desktop.LoadFromSettings();
            RebuildDesktopColors();
        }

        if (section == SettingsSection.DesktopMemory && MemoryHost.Content is DesktopMemorySettingsPage memory)
            memory.RefreshDesktops();
        PageScroll.ScrollToTop();
    }

    private void EnsureSectionContent(SettingsSection section)
    {
        switch (section)
        {
            case SettingsSection.GitWatcher when GitOverlayHost.Content is null:
                GitOverlayHost.Content = new GitOverlaySettingsPage(_service, _settings, _applyGit);
                break;
            case SettingsSection.VirtualDesktops when DesktopOverlayHost.Content is null:
                DesktopOverlayHost.Content = new DesktopOverlaySettingsPage(_service, _settings, OnDesktopAppearanceChanged);
                break;
            case SettingsSection.DesktopMemory when MemoryHost.Content is null:
                MemoryHost.Content = new DesktopMemorySettingsPage(new DesktopMemoryStore(
                    Path.Combine(StoragePath.Text, "desktop-memory", "snapshots.json")));
                break;
            case SettingsSection.StickyNotes when NotesHost.Content is null:
                NotesHost.Content = new StickyNoteSettingsPage(_service, _settings, _stickyNotes);
                break;
        }
    }

    internal void RefreshRepositoryState()
    {
        if (!_ready) return;
        PinnedMode.IsChecked = _settings.WatchMode == RepositoryWatchMode.PinnedRepo;
        WorkspaceMode.IsChecked = _settings.WatchMode == RepositoryWatchMode.WorkspaceRepo;
        RepositoryPath.Text = _settings.WatchedRepositoryPath ?? "Not selected";
        RepositoryPath.ToolTip = RepositoryPath.Text;
        WorkspacePath.Text = _settings.WorkspaceRootPath ?? "Not selected";
        WorkspacePath.ToolTip = WorkspacePath.Text;
        GitOverlayCheck.IsChecked = _settings.OverlayVisible;
    }

    internal void RefreshExternalSettings()
    {
        if (!_ready) return;
        RefreshRepositoryState();
        if (DesktopOverlayHost.Content is DesktopOverlaySettingsPage desktop) desktop.LoadFromSettings();
        if (DesktopsPage.Visibility == Visibility.Visible)
        {
            RebuildDesktopColors();
        }
    }

    private void OnDesktopAppearanceChanged()
    {
        _desktops.RefreshSettings();
        if (DesktopsPage.Visibility == Visibility.Visible)
        {
            RebuildDesktopColors();
        }
    }

    private void OnDesktopStateChanged(object? sender, VirtualDesktopDisplayState state)
    {
        if (!_ready) return;
        if (MemoryPage.Visibility == Visibility.Visible && MemoryHost.Content is DesktopMemorySettingsPage memory)
            memory.RefreshDesktops();
        if (DesktopsPage.Visibility == Visibility.Visible)
        {
            RebuildDesktopColors();
        }
    }

    private void RebuildDesktopColors()
    {
        DesktopColorRows.Children.Clear();
        var state = _desktops.Monitor.CurrentState ?? VirtualDesktopRegistryReader.TryGetDisplayState();
        if (state is null || state.AllDesktops.Count == 0)
        {
            DesktopColorRows.Children.Add(new WpfTextBlock { Text = "No virtual desktops detected.", Foreground = Brush("#617083") });
            return;
        }

        foreach (var desktop in state.AllDesktops)
        {
            var color = VirtualDesktopColors.Resolve(_settings, desktop.Id);
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = true };
            var reset = new WpfButton { Content = "Reset", IsEnabled = _settings.VirtualDesktopFontColors.ContainsKey(desktop.Id), Margin = new Thickness(8, 0, 0, 0) };
            reset.Click += (_, _) => ChangeDesktopColor(desktop.Id, null);
            DockPanel.SetDock(reset, Dock.Right);
            row.Children.Add(reset);

            var change = new WpfButton { Content = "Change", Margin = new Thickness(8, 0, 0, 0) };
            change.Click += (_, _) => ChooseDesktopColor(desktop.Id);
            DockPanel.SetDock(change, Dock.Right);
            row.Children.Add(change);

            var swatch = new WpfBorder { Width = 24, Height = 24, CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(color), BorderBrush = Brush("#B5C1D1"), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 10, 0) };
            DockPanel.SetDock(swatch, Dock.Left);
            row.Children.Add(swatch);
            var label = new WpfTextBlock { Text = desktop.DisplayName, VerticalAlignment = VerticalAlignment.Center, ToolTip = OverlaySettings.ToHexColor(color), TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
            row.Children.Add(label);
            DesktopColorRows.Children.Add(row);
        }
    }

    private void ChooseDesktopColor(Guid id)
    {
        var color = VirtualDesktopColors.Resolve(_settings, id);
        using var dialog = new Forms.ColorDialog { FullOpen = true, Color = System.Drawing.Color.FromArgb(color.R, color.G, color.B) };
        if (dialog.ShowDialog(new WpfDialogOwner(this)) == Forms.DialogResult.OK)
        {
            ChangeDesktopColor(id, OverlaySettings.ToHexColor(System.Windows.Media.Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B)));
        }
    }

    private void ChangeDesktopColor(Guid id, string? hex)
    {
        var hadPrevious = _settings.VirtualDesktopFontColors.TryGetValue(id, out var previous);
        if (hex is null) _settings.VirtualDesktopFontColors.Remove(id);
        else _settings.VirtualDesktopFontColors[id] = hex;
        try
        {
            _service.Save(_settings);
        }
        catch (Exception ex)
        {
            if (hadPrevious) _settings.VirtualDesktopFontColors[id] = previous!;
            else _settings.VirtualDesktopFontColors.Remove(id);
            System.Windows.MessageBox.Show(this, ex.Message, "Could not save desktop color", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        OnDesktopAppearanceChanged();
    }

    private static SolidColorBrush Brush(string value) =>
        new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value)!);

    private void OnStartupClick(object sender, RoutedEventArgs e)
    {
        if (StartupCheck.IsChecked != _settings.StartWithWindows) _toggleStartup();
        StartupCheck.IsChecked = _settings.StartWithWindows;
    }

    private void OnGitOverlayClick(object sender, RoutedEventArgs e)
    {
        var previous = _settings.OverlayVisible;
        try { GitOverlayVisibility.Set(_settings, GitOverlayCheck.IsChecked == true, _service.Save); }
        catch (Exception ex)
        {
            GitOverlayCheck.IsChecked = previous;
            System.Windows.MessageBox.Show(this, ex.Message, "Could not save overlay setting", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        _applyGit();
    }

    private void OnPinnedMode(object sender, RoutedEventArgs e) { _pinnedMode(); RefreshRepositoryState(); }
    private void OnWorkspaceMode(object sender, RoutedEventArgs e) { _workspaceMode(); RefreshRepositoryState(); }
    private void OnChooseRepository(object sender, RoutedEventArgs e) { _chooseRepository(); RefreshRepositoryState(); }
    private void OnChooseWorkspace(object sender, RoutedEventArgs e) { _chooseWorkspace(); RefreshRepositoryState(); }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(StoragePath.Text);
            Process.Start(new ProcessStartInfo(StoragePath.Text) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "Could not open data folder", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnOpenRepository(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://github.com/markrai/branchwatch") { UseShellExecute = true }); }
        catch (Exception ex) { System.Windows.MessageBox.Show(this, ex.Message, "Could not open repository", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}

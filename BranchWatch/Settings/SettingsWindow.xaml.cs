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
    private readonly DesktopOverlaySettingsPage _desktopPage;
    private readonly GitOverlaySettingsPage _gitPage;
    private readonly DesktopMemorySettingsPage _memoryPage;
    private bool _ready;

    internal SettingsWindow(SettingsService service, AppSettings settings, StickyNotesController stickyNotes,
        VirtualDesktopOverlayController desktops, Action applyGit, Action chooseRepository,
        Action chooseWorkspace, Action pinnedMode, Action workspaceMode, Action toggleStartup)
    {
        _service = service;
        _settings = settings;
        _desktops = desktops;
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

        _gitPage = new GitOverlaySettingsPage(service, settings, applyGit);
        _desktopPage = new DesktopOverlaySettingsPage(service, settings, OnDesktopAppearanceChanged);
        GitOverlayHost.Content = _gitPage;
        DesktopOverlayHost.Content = _desktopPage;
        NotesHost.Content = new StickyNoteSettingsPage(service, settings, stickyNotes);
        _memoryPage = new DesktopMemorySettingsPage(new DesktopMemoryStore(Path.Combine(StoragePath.Text, "desktop-memory", "snapshots.json")));
        MemoryHost.Content = _memoryPage;

        _desktops.Monitor.StateChanged += OnDesktopStateChanged;
        Closed += (_, _) =>
        {
            _desktops.Monitor.StateChanged -= OnDesktopStateChanged;
            _memoryPage.Stop();
        };
        _ready = true;
        Navigate(SettingsSection.General);
    }

    internal void Navigate(SettingsSection section)
    {
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
        if (section == SettingsSection.VirtualDesktops)
        {
            _desktopPage.LoadFromSettings();
            RebuildDesktopColors();
        }

        if (section == SettingsSection.DesktopMemory) _memoryPage.RefreshDesktops();
        PageScroll.ScrollToTop();
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
        _desktopPage.LoadFromSettings();
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
        if (MemoryPage.Visibility == Visibility.Visible) _memoryPage.RefreshDesktops();
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
        _settings.OverlayVisible = GitOverlayCheck.IsChecked == true;
        try { _service.Save(_settings); }
        catch (Exception ex)
        {
            _settings.OverlayVisible = previous;
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

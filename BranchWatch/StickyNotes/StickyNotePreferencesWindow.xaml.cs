using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BranchWatch;

public partial class StickyNotePreferencesWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly StickyNotesController _controller;
    private bool _loading = true;

    internal StickyNotePreferencesWindow(SettingsService settingsService, AppSettings settings, StickyNotesController controller)
    {
        _settingsService = settingsService;
        _settings = settings;
        _controller = controller;
        InitializeComponent();

        MaxHeight = SystemParameters.WorkArea.Height * 0.95;
        MaxWidth = SystemParameters.WorkArea.Width * 0.95;
        var appearance = StickyNoteAppearance.FromSettings(settings);
        ColorBox.ItemsSource = StickyNoteAppearance.Palette;
        ColorBox.SelectedValue = appearance.Color;
        TransparencySlider.Value = appearance.TransparencyLevel;
        ApplyAllButton.IsEnabled = controller.IsAvailable;
        _loading = false;
        UpdatePreview();
    }

    private StickyNoteAppearance SelectedAppearance =>
        new(ColorBox.SelectedValue as string ?? "Yellow", (int)TransparencySlider.Value);

    private void OnColorChanged(object sender, SelectionChangedEventArgs e) => SelectionChanged();

    private void OnTransparencyChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => SelectionChanged();

    private void SelectionChanged()
    {
        if (_loading)
        {
            return;
        }

        StatusText.Text = "Unsaved changes.";
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var appearance = SelectedAppearance;
        var color = StickyNoteAppearance.ResolveColor(appearance.Color);
        PreviewSurface.Background = BrushFor(color.Body);
        PreviewHeader.Background = BrushFor(color.Header);
        var opacity = StickyNoteTransparency.OpacityForLevel(appearance.TransparencyLevel);
        PreviewSurface.Opacity = opacity;
        TransparencyLabel.Text = $"Level {appearance.TransparencyLevel} · {opacity:P0} opaque";
    }

    private static SolidColorBrush BrushFor(string value) =>
        new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value)!);

    private bool SaveDefaults()
    {
        try
        {
            SelectedAppearance.SaveDefaults(_settingsService, _settings);
            StatusText.Text = "Defaults saved for new notes.";
            return true;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not save defaults: {ex.Message}";
            return false;
        }
    }

    private void OnSaveDefaults(object sender, RoutedEventArgs e) => SaveDefaults();

    private void OnApplyAll(object sender, RoutedEventArgs e)
    {
        if (!SaveDefaults())
        {
            return;
        }

        StatusText.Text = _controller.ApplyAppearanceToAll(SelectedAppearance)
            ? $"Defaults saved and applied to {_controller.Notes.Count} notes."
            : "Defaults saved. Existing note changes could not be saved yet.";
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}

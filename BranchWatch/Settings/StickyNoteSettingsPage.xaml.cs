using System.Windows;
using System.Windows.Controls;

namespace BranchWatch;

public partial class StickyNoteSettingsPage : System.Windows.Controls.UserControl
{
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly StickyNotesController _controller;
    private bool _loading = true;

    internal StickyNoteSettingsPage(SettingsService settingsService, AppSettings settings, StickyNotesController controller)
    {
        _settingsService = settingsService;
        _settings = settings;
        _controller = controller;
        InitializeComponent();

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
        PreviewSurface.Background = StickyNoteRenderingResources.BrushFor(color.Body);
        PreviewHeader.Background = StickyNoteRenderingResources.BrushFor(color.Header);
        var opacity = StickyNoteTransparency.OpacityForLevel(appearance.TransparencyLevel);
        PreviewSurface.Opacity = opacity;
        TransparencyLabel.Text = $"Level {appearance.TransparencyLevel} · {opacity:P0} opaque";
    }

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

}

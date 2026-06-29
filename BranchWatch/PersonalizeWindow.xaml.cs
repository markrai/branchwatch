using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace BranchWatch;

public partial class PersonalizeWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly Action _onSettingsChanged;
    private bool _isLoading;

    public PersonalizeWindow(SettingsService settingsService, AppSettings settings, Action onSettingsChanged)
    {
        _settingsService = settingsService;
        _settings = settings;
        _onSettingsChanged = onSettingsChanged;
        _isLoading = true;

        InitializeComponent();
        ApplyScreenBounds();
        Loaded += OnWindowLoaded;
        LoadFromSettings();
    }

    private void ApplyScreenBounds()
    {
        var workArea = SystemParameters.WorkArea;
        MaxHeight = workArea.Height * 0.9;
        MaxWidth = Math.Min(420, workArea.Width * 0.95);
        ContentScrollViewer.MaxHeight = MaxHeight - 16;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnWindowLoaded;

        var workArea = SystemParameters.WorkArea;
        if (ActualHeight > MaxHeight)
        {
            SizeToContent = SizeToContent.Manual;
            Height = MaxHeight;
        }

        Left = Math.Max(workArea.Left, Math.Min(Left, workArea.Right - Width));
        Top = Math.Max(workArea.Top, Math.Min(Top, workArea.Bottom - ActualHeight));
    }

    private void LoadFromSettings()
    {
        switch (_settings.OverlayPositionPreset?.Trim().ToLowerInvariant())
        {
            case "top-left":
                TopLeftRadio.IsChecked = true;
                break;
            case "bottom-left":
                BottomLeftRadio.IsChecked = true;
                break;
            case "bottom-right":
                BottomRightRadio.IsChecked = true;
                break;
            case "top-right":
            default:
                TopRightRadio.IsChecked = true;
                break;
        }

        ShowOutlineCheckBox.IsChecked = _settings.OverlayShowOutline;
        ShowRepositoryNameCheckBox.IsChecked = _settings.OverlayShowRepositoryName;
        if (_settings.OverlayRepositoryFullPath)
        {
            RepositoryFullPathRadio.IsChecked = true;
        }
        else
        {
            RepositoryFolderRadio.IsChecked = true;
        }
        UpdateRepositoryLabelPanelState();
        SizeSlider.Value = OverlaySettings.ClampScale(_settings.OverlayScale);
        UpdateSizeLabel(SizeSlider.Value);
        OpacitySlider.Value = OverlaySettings.ClampOpacity(_settings.OverlayOpacity);
        UpdateOpacityLabel(OpacitySlider.Value);
        ForegroundOpacitySlider.Value = OverlaySettings.ClampForegroundOpacity(_settings.OverlayForegroundOpacity);
        UpdateForegroundOpacityLabel(ForegroundOpacitySlider.Value);
        UpdateFontColorPreview(OverlaySettings.ParseFontColor(_settings.OverlayFontColor));

        _isLoading = false;
    }

    private void OnSettingChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        _settings.OverlayPositionPreset = GetSelectedPosition();
        _settings.OverlayShowOutline = ShowOutlineCheckBox.IsChecked == true;
        _settings.OverlayShowRepositoryName = ShowRepositoryNameCheckBox.IsChecked == true;
        _settings.OverlayRepositoryFullPath = RepositoryFullPathRadio.IsChecked == true;
        UpdateRepositoryLabelPanelState();
        SaveAndApply();
    }

    private void UpdateRepositoryLabelPanelState()
    {
        RepositoryLabelPanel.IsEnabled = ShowRepositoryNameCheckBox.IsChecked == true;
    }

    private void OnSizeSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoading)
        {
            return;
        }

        _settings.OverlayScale = OverlaySettings.ClampScale(SizeSlider.Value);
        UpdateSizeLabel(_settings.OverlayScale);
        SaveAndApply();
    }

    private void OnOpacitySliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoading)
        {
            return;
        }

        _settings.OverlayOpacity = OverlaySettings.ClampOpacity(OpacitySlider.Value);
        UpdateOpacityLabel(_settings.OverlayOpacity);
        SaveAndApply();
    }

    private void OnForegroundOpacitySliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoading)
        {
            return;
        }

        _settings.OverlayForegroundOpacity = OverlaySettings.ClampForegroundOpacity(ForegroundOpacitySlider.Value);
        UpdateForegroundOpacityLabel(_settings.OverlayForegroundOpacity);
        SaveAndApply();
    }

    private void OnChooseColorClick(object sender, RoutedEventArgs e)
    {
        var current = OverlaySettings.ParseFontColor(_settings.OverlayFontColor);
        using var dialog = new Forms.ColorDialog
        {
            Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B),
            FullOpen = true
        };

        if (dialog.ShowDialog() != Forms.DialogResult.OK)
        {
            return;
        }

        _settings.OverlayFontColor = OverlaySettings.ToHexColor(
            System.Windows.Media.Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B));
        UpdateFontColorPreview(OverlaySettings.ParseFontColor(_settings.OverlayFontColor));
        SaveAndApply();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private string GetSelectedPosition()
    {
        if (TopLeftRadio.IsChecked == true)
        {
            return "top-left";
        }

        if (BottomLeftRadio.IsChecked == true)
        {
            return "bottom-left";
        }

        if (BottomRightRadio.IsChecked == true)
        {
            return "bottom-right";
        }

        return "top-right";
    }

    private void UpdateSizeLabel(double scale)
    {
        SizeLabel.Text = $"{Math.Round(scale * 100)}% (right = largest)";
    }

    private void UpdateOpacityLabel(double opacity)
    {
        OpacityLabel.Text = $"{Math.Round(opacity * 100)}% opaque (right = most opaque)";
    }

    private void UpdateForegroundOpacityLabel(double opacity)
    {
        ForegroundOpacityLabel.Text = $"{Math.Round(opacity * 100)}% opaque (left = most transparent)";
    }

    private void UpdateFontColorPreview(System.Windows.Media.Color color)
    {
        FontColorPreview.Background = new SolidColorBrush(color);
    }

    private void SaveAndApply()
    {
        _settingsService.Save(_settings);
        _onSettingsChanged();
    }
}

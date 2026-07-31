using System.Windows;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace BranchWatch;

public partial class VirtualDesktopsWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly Action _onSettingsChanged;
    private bool _isLoading;

    public VirtualDesktopsWindow(SettingsService settingsService, AppSettings settings, Action onSettingsChanged)
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
        ShowOverlayCheckBox.IsChecked = _settings.VirtualDesktopOverlayVisible;
        ShowOnlyOnDesktopCheckBox.IsChecked = _settings.VirtualDesktopOverlayShowOnlyOnDesktop;

        switch (_settings.VirtualDesktopOverlayPositionPreset?.Trim().ToLowerInvariant())
        {
            case "top-right":
                TopRightRadio.IsChecked = true;
                break;
            case "bottom-left":
                BottomLeftRadio.IsChecked = true;
                break;
            case "bottom-right":
                BottomRightRadio.IsChecked = true;
                break;
            case "show-on-taskbar":
                TaskbarRadio.IsChecked = true;
                break;
            case "top-left":
            default:
                TopLeftRadio.IsChecked = true;
                break;
        }

        ShowOutlineCheckBox.IsChecked = _settings.VirtualDesktopOverlayShowOutline;
        SizeSlider.Value = OverlaySettings.ClampScale(_settings.VirtualDesktopOverlayScale);
        UpdateSizeLabel(SizeSlider.Value);
        OpacitySlider.Value = OverlaySettings.ClampOpacity(_settings.VirtualDesktopOverlayOpacity);
        UpdateOpacityLabel(OpacitySlider.Value);
        ForegroundOpacitySlider.Value = OverlaySettings.ClampForegroundOpacity(_settings.VirtualDesktopOverlayForegroundOpacity);
        UpdateForegroundOpacityLabel(ForegroundOpacitySlider.Value);
        UpdateFontColorPreview(OverlaySettings.ParseFontColor(_settings.VirtualDesktopOverlayFontColor));

        _isLoading = false;
    }

    private void OnSettingChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        _settings.VirtualDesktopOverlayVisible = ShowOverlayCheckBox.IsChecked == true;
        _settings.VirtualDesktopOverlayShowOnlyOnDesktop = ShowOnlyOnDesktopCheckBox.IsChecked == true;
        _settings.VirtualDesktopOverlayPositionPreset = GetSelectedPosition();
        _settings.VirtualDesktopOverlayShowOutline = ShowOutlineCheckBox.IsChecked == true;
        SaveAndApply();
    }

    private void OnSizeSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoading)
        {
            return;
        }

        _settings.VirtualDesktopOverlayScale = OverlaySettings.ClampScale(SizeSlider.Value);
        UpdateSizeLabel(_settings.VirtualDesktopOverlayScale);
        SaveAndApply();
    }

    private void OnOpacitySliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoading)
        {
            return;
        }

        _settings.VirtualDesktopOverlayOpacity = OverlaySettings.ClampOpacity(OpacitySlider.Value);
        UpdateOpacityLabel(_settings.VirtualDesktopOverlayOpacity);
        SaveAndApply();
    }

    private void OnForegroundOpacitySliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoading)
        {
            return;
        }

        _settings.VirtualDesktopOverlayForegroundOpacity = OverlaySettings.ClampForegroundOpacity(ForegroundOpacitySlider.Value);
        UpdateForegroundOpacityLabel(_settings.VirtualDesktopOverlayForegroundOpacity);
        SaveAndApply();
    }

    private void OnChooseColorClick(object sender, RoutedEventArgs e)
    {
        var current = OverlaySettings.ParseFontColor(_settings.VirtualDesktopOverlayFontColor);
        using var dialog = new Forms.ColorDialog
        {
            Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B),
            FullOpen = true
        };

        if (dialog.ShowDialog(new WpfDialogOwner(this)) != Forms.DialogResult.OK)
        {
            return;
        }

        _settings.VirtualDesktopOverlayFontColor = OverlaySettings.ToHexColor(
            System.Windows.Media.Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B));
        UpdateFontColorPreview(OverlaySettings.ParseFontColor(_settings.VirtualDesktopOverlayFontColor));
        SaveAndApply();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private string GetSelectedPosition()
    {
        if (TopRightRadio.IsChecked == true)
        {
            return "top-right";
        }

        if (BottomLeftRadio.IsChecked == true)
        {
            return "bottom-left";
        }

        if (BottomRightRadio.IsChecked == true)
        {
            return "bottom-right";
        }

        if (TaskbarRadio.IsChecked == true)
        {
            return "show-on-taskbar";
        }

        return "top-left";
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

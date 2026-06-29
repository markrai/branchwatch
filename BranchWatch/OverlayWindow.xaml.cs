using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace BranchWatch;

public partial class OverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExLayered = 0x00080000;
    private const long WsExNoActivate = 0x08000000;
    private const double OutlineBorderSize = 2;
    private const double BaseCornerRadius = 8;
    private const double BaseLineGap = 4;
    private const double ScreenMargin = 24;
    private const double RepositoryFontScale = 0.5;

    private string? _repositoryRoot;

    public OverlayWindow()
    {
        InitializeComponent();
    }

    public void SetOverlayText(string branch, string? repositoryRoot)
    {
        BranchText.Text = string.IsNullOrWhiteSpace(branch) ? "Unknown branch" : branch;
        _repositoryRoot = repositoryRoot;
    }

    public void ApplySettings(AppSettings settings)
    {
        var scale = OverlaySettings.ClampScale(settings.OverlayScale);
        BranchText.FontSize = OverlaySettings.BaseFontSize * scale;

        var paddingH = OverlaySettings.BasePaddingHorizontal * scale;
        var paddingV = OverlaySettings.BasePaddingVertical * scale;
        RootBorder.Padding = new Thickness(paddingH, paddingV, paddingH, paddingV);
        RootBorder.CornerRadius = new CornerRadius(BaseCornerRadius * scale);

        var opacity = OverlaySettings.ClampOpacity(settings.OverlayOpacity);
        var alpha = (byte)Math.Round(opacity * 255);
        RootBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, 20, 24, 32));

        RootBorder.BorderThickness = settings.OverlayShowOutline ? new Thickness(1) : new Thickness(0);

        var fontColor = OverlaySettings.ParseFontColor(settings.OverlayFontColor);
        var fontOpacity = OverlaySettings.ClampForegroundOpacity(settings.OverlayForegroundOpacity);
        var fontAlpha = (byte)Math.Round(fontOpacity * 255);
        var foreground = new SolidColorBrush(
            System.Windows.Media.Color.FromArgb(fontAlpha, fontColor.R, fontColor.G, fontColor.B));
        BranchText.Foreground = foreground;

        var showRepository = settings.OverlayShowRepositoryName;
        RepositoryText.Visibility = showRepository ? Visibility.Visible : Visibility.Collapsed;
        if (showRepository)
        {
            RepositoryText.Text = GetRepositoryDisplayName(_repositoryRoot, settings.OverlayRepositoryFullPath);
            RepositoryText.FontSize = BranchText.FontSize * RepositoryFontScale;
            RepositoryText.Foreground = foreground;
            RepositoryText.Margin = new Thickness(0, BaseLineGap * scale, 0, 0);
        }

        UpdateSize(settings, scale);
        Position(settings.OverlayPositionPreset);
    }

    public void ShowOverlay(AppSettings settings)
    {
        ApplySettings(settings);
        if (!IsVisible)
        {
            Show();
        }

        Topmost = false;
        Topmost = true;
        ActivateClickThrough();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ActivateClickThrough();
    }

    private static string GetRepositoryDisplayName(string? repositoryRoot, bool fullPath)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot))
        {
            return "(none)";
        }

        if (fullPath)
        {
            return repositoryRoot;
        }

        var trimmed = repositoryRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var folderName = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(folderName) ? trimmed : folderName;
    }

    private void UpdateSize(AppSettings settings, double scale)
    {
        var foreground = BranchText.Foreground as SolidColorBrush ?? System.Windows.Media.Brushes.White;
        var branchTypeface = new Typeface(
            BranchText.FontFamily, BranchText.FontStyle, BranchText.FontWeight, BranchText.FontStretch);
        var branchFormatted = MeasureText(BranchText.Text, branchTypeface, BranchText.FontSize, foreground);

        var horizontalPadding = OverlaySettings.BasePaddingHorizontal * scale * 2;
        var verticalPadding = OverlaySettings.BasePaddingVertical * scale * 2;
        var borderSize = settings.OverlayShowOutline ? OutlineBorderSize : 0;
        var workArea = SystemParameters.WorkArea;
        var maxContentWidth = workArea.Width - (ScreenMargin * 2) - horizontalPadding - borderSize;

        var contentWidth = Math.Ceiling(branchFormatted.WidthIncludingTrailingWhitespace);
        var contentHeight = Math.Ceiling(branchFormatted.Height);

        var showRepository = settings.OverlayShowRepositoryName;
        if (showRepository)
        {
            var repoTypeface = new Typeface(
                RepositoryText.FontFamily, RepositoryText.FontStyle, RepositoryText.FontWeight, RepositoryText.FontStretch);
            var repoFormatted = MeasureText(RepositoryText.Text, repoTypeface, RepositoryText.FontSize, foreground);
            contentWidth = Math.Max(contentWidth, Math.Ceiling(repoFormatted.WidthIncludingTrailingWhitespace));
            contentHeight += (BaseLineGap * scale) + Math.Ceiling(repoFormatted.Height);
        }

        ApplyMaxWidth(BranchText, contentWidth, maxContentWidth);
        if (showRepository)
        {
            ApplyMaxWidth(RepositoryText, contentWidth, maxContentWidth);
        }
        else
        {
            RepositoryText.ClearValue(FrameworkElement.MaxWidthProperty);
            RepositoryText.TextTrimming = TextTrimming.None;
        }

        if (contentWidth > maxContentWidth)
        {
            contentWidth = maxContentWidth;
        }

        Width = contentWidth + horizontalPadding + borderSize;
        Height = contentHeight + verticalPadding + borderSize;
    }

    private FormattedText MeasureText(string text, Typeface typeface, double fontSize, System.Windows.Media.Brush foreground)
    {
        return new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            typeface,
            fontSize,
            foreground,
            GetPixelsPerDip());
    }

    private static void ApplyMaxWidth(TextBlock textBlock, double contentWidth, double maxContentWidth)
    {
        if (contentWidth > maxContentWidth)
        {
            textBlock.MaxWidth = maxContentWidth;
            textBlock.TextTrimming = TextTrimming.CharacterEllipsis;
        }
        else
        {
            textBlock.ClearValue(FrameworkElement.MaxWidthProperty);
            textBlock.TextTrimming = TextTrimming.None;
        }
    }

    private double GetPixelsPerDip()
    {
        try
        {
            return VisualTreeHelper.GetDpi(this).PixelsPerDip;
        }
        catch
        {
            return 1.0;
        }
    }

    private void Position(string? preset)
    {
        var workArea = SystemParameters.WorkArea;

        switch (preset?.Trim().ToLowerInvariant())
        {
            case "top-left":
                Left = workArea.Left + ScreenMargin;
                Top = workArea.Top + ScreenMargin;
                break;
            case "bottom-right":
                Left = workArea.Right - Width - ScreenMargin;
                Top = workArea.Bottom - Height - ScreenMargin;
                break;
            case "bottom-left":
                Left = workArea.Left + ScreenMargin;
                Top = workArea.Bottom - Height - ScreenMargin;
                break;
            case "top-right":
            default:
                Left = workArea.Right - Width - ScreenMargin;
                Top = workArea.Top + ScreenMargin;
                break;
        }
    }

    private void ActivateClickThrough()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style | WsExTransparent | WsExToolWindow | WsExLayered | WsExNoActivate));
    }

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(hWnd, nIndex)
            : GetWindowLongPtr32(hWnd, nIndex);
    }

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
            : SetWindowLongPtr32(hWnd, nIndex, dwNewLong);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern IntPtr GetWindowLongPtr32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern IntPtr SetWindowLongPtr32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}

using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
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
    private const double HorizontalPadding = 36;
    private const double VerticalPadding = 16;
    private const double BorderSize = 2;
    private const double ScreenMargin = 24;

    public OverlayWindow()
    {
        InitializeComponent();
    }

    public void ApplySettings(AppSettings settings)
    {
        BranchText.FontSize = Math.Clamp(settings.OverlayFontSize, 18, 96);

        var opacity = Math.Clamp(settings.OverlayOpacity, 0.2, 1.0);
        var alpha = (byte)Math.Round(opacity * 255);
        RootBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, 20, 24, 32));

        UpdateSize();
        Position(settings.OverlayPositionPreset);
    }

    public void SetBranchText(string branch)
    {
        BranchText.Text = string.IsNullOrWhiteSpace(branch) ? "Unknown branch" : branch;
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

    private void UpdateSize()
    {
        var fontSize = BranchText.FontSize;
        var typeface = new Typeface(BranchText.FontFamily, BranchText.FontStyle, BranchText.FontWeight, BranchText.FontStretch);
        var formattedText = new FormattedText(
            BranchText.Text,
            CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            typeface,
            fontSize,
            System.Windows.Media.Brushes.White,
            GetPixelsPerDip());

        var workArea = SystemParameters.WorkArea;
        var maxContentWidth = workArea.Width - (ScreenMargin * 2) - HorizontalPadding - BorderSize;
        var contentWidth = Math.Ceiling(formattedText.WidthIncludingTrailingWhitespace);
        var contentHeight = Math.Ceiling(formattedText.Height);

        if (contentWidth > maxContentWidth)
        {
            contentWidth = maxContentWidth;
            BranchText.MaxWidth = maxContentWidth;
            BranchText.TextTrimming = TextTrimming.CharacterEllipsis;
        }
        else
        {
            BranchText.ClearValue(FrameworkElement.MaxWidthProperty);
            BranchText.TextTrimming = TextTrimming.None;
        }

        Width = contentWidth + HorizontalPadding + BorderSize;
        Height = contentHeight + VerticalPadding + BorderSize;
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

using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace BranchWatch;

public partial class VirtualDesktopOverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int SwpNoActivate = 0x0010;
    private const int SwpNomove = 0x0002;
    private const int SwpNosize = 0x0001;
    private const long WsExTransparent = 0x00000020;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExLayered = 0x00080000;
    private const long WsExNoActivate = 0x08000000;
    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNotopmost = new(-2);
    private const double OutlineBorderSize = 2;
    private const double BaseCornerRadius = 8;
    private const double ScreenMargin = 24;
    private const double DesktopFontScale = 0.67;

    private string _displayName = "Desktop 1";
    private DispatcherTimer? _taskbarZOrderTimer;

    public VirtualDesktopOverlayWindow()
    {
        InitializeComponent();
    }

    public void SetDesktopName(string displayName)
    {
        _displayName = string.IsNullOrWhiteSpace(displayName) ? "Desktop 1" : displayName;
        DesktopText.Text = _displayName;
    }

    public void ApplySettings(AppSettings settings)
    {
        var scale = OverlaySettings.ClampScale(settings.VirtualDesktopOverlayScale);
        DesktopText.FontSize = OverlaySettings.BaseFontSize * scale * DesktopFontScale;

        var paddingH = OverlaySettings.BasePaddingHorizontal * scale;
        var paddingV = OverlaySettings.BasePaddingVertical * scale;
        RootBorder.Padding = new Thickness(paddingH, paddingV, paddingH, paddingV);
        RootBorder.CornerRadius = new CornerRadius(BaseCornerRadius * scale);

        var opacity = OverlaySettings.ClampOpacity(settings.VirtualDesktopOverlayOpacity);
        var alpha = (byte)Math.Round(opacity * 255);
        RootBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, 20, 24, 32));

        RootBorder.BorderThickness = settings.VirtualDesktopOverlayShowOutline ? new Thickness(1) : new Thickness(0);

        var fontColor = OverlaySettings.ParseFontColor(settings.VirtualDesktopOverlayFontColor);
        var fontOpacity = OverlaySettings.ClampForegroundOpacity(settings.VirtualDesktopOverlayForegroundOpacity);
        var fontAlpha = (byte)Math.Round(fontOpacity * 255);
        DesktopText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(fontAlpha, fontColor.R, fontColor.G, fontColor.B));

        DesktopText.Text = _displayName;
        UpdateSize(settings, scale);
        Position(settings.VirtualDesktopOverlayPositionPreset);
    }

    public void ShowOverlay(AppSettings settings)
    {
        ApplySettings(settings);
        if (!IsVisible)
        {
            Show();
        }

        ApplyTopmostPolicy(settings);
        ActivateClickThrough();
    }

    protected override void OnClosed(EventArgs e)
    {
        StopTaskbarZOrderTimer();
        base.OnClosed(e);
    }

    private void ApplyTopmostPolicy(AppSettings settings)
    {
        StopTaskbarZOrderTimer();

        if (IsTaskbarPosition(settings.VirtualDesktopOverlayPositionPreset))
        {
            EnsureAboveTaskbar();
            StartTaskbarZOrderTimer();
            return;
        }

        if (settings.VirtualDesktopOverlayShowOnlyOnDesktop)
        {
            Topmost = false;
            SetNativeTopmost(false);
        }
        else
        {
            Topmost = false;
            Topmost = true;
        }
    }

    private static bool IsTaskbarPosition(string? preset) =>
        string.Equals(preset?.Trim(), "show-on-taskbar", StringComparison.OrdinalIgnoreCase);

    private void EnsureAboveTaskbar()
    {
        Topmost = true;
        SetNativeTopmost(true);
    }

    private void SetNativeTopmost(bool topmost)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        SetWindowPos(
            handle,
            topmost ? HwndTopmost : HwndNotopmost,
            0,
            0,
            0,
            0,
            SwpNomove | SwpNosize | SwpNoActivate);
    }

    private void StartTaskbarZOrderTimer()
    {
        _taskbarZOrderTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };
        _taskbarZOrderTimer.Tick += OnTaskbarZOrderTimerTick;
        _taskbarZOrderTimer.Start();
    }

    private void StopTaskbarZOrderTimer()
    {
        if (_taskbarZOrderTimer is null)
        {
            return;
        }

        _taskbarZOrderTimer.Tick -= OnTaskbarZOrderTimerTick;
        _taskbarZOrderTimer.Stop();
        _taskbarZOrderTimer = null;
    }

    private void OnTaskbarZOrderTimerTick(object? sender, EventArgs e)
    {
        if (!IsVisible)
        {
            return;
        }

        EnsureAboveTaskbar();
    }

    private void UpdateSize(AppSettings settings, double scale)
    {
        var foreground = DesktopText.Foreground as SolidColorBrush ?? System.Windows.Media.Brushes.White;
        var horizontalPadding = OverlaySettings.BasePaddingHorizontal * scale * 2;
        var verticalPadding = OverlaySettings.BasePaddingVertical * scale * 2;
        var borderSize = settings.VirtualDesktopOverlayShowOutline ? OutlineBorderSize : 0;
        var workArea = SystemParameters.WorkArea;
        var maxContentWidth = workArea.Width - (ScreenMargin * 2) - horizontalPadding - borderSize;

        var formatted = MeasureText(DesktopText.Text, new Typeface(
            DesktopText.FontFamily, DesktopText.FontStyle, DesktopText.FontWeight, DesktopText.FontStretch),
            DesktopText.FontSize, foreground);

        var contentWidth = Math.Ceiling(formatted.WidthIncludingTrailingWhitespace);
        var contentHeight = Math.Ceiling(formatted.Height);

        if (contentWidth > maxContentWidth)
        {
            DesktopText.MaxWidth = maxContentWidth;
            DesktopText.TextTrimming = TextTrimming.CharacterEllipsis;
            contentWidth = maxContentWidth;
        }
        else
        {
            DesktopText.ClearValue(FrameworkElement.MaxWidthProperty);
            DesktopText.TextTrimming = TextTrimming.None;
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
                Left = workArea.Right - Width - ScreenMargin;
                Top = workArea.Top + ScreenMargin;
                break;
            case "show-on-taskbar":
                TaskbarOverlayPosition.Apply(this);
                break;
            default:
                Left = workArea.Left + ScreenMargin;
                Top = workArea.Top + ScreenMargin;
                break;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ActivateClickThrough();
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);
}

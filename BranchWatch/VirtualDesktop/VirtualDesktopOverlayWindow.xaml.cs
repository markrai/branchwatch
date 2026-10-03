using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
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
    private const double TaskbarEdgeReserve = 120;

    private string _displayName = "Desktop 1";
    private VirtualDesktopDisplayState? _displayState;
    private DispatcherTimer? _taskbarZOrderTimer;
    private double _activeWidth;
    private double _activeHeight;
    private double _activeOffsetX;
    private double _activeOffsetY;

    public VirtualDesktopOverlayWindow()
    {
        InitializeComponent();
    }

    public void SetDesktopName(string displayName)
    {
        _displayName = string.IsNullOrWhiteSpace(displayName) ? "Desktop 1" : displayName;
        DesktopText.Text = _displayName;
    }

    public void SetDisplayState(VirtualDesktopDisplayState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _displayState = state;
        SetDesktopName(state.ActiveDesktop.DisplayName);
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

        var typeface = new Typeface(
            DesktopText.FontFamily, DesktopText.FontStyle, DesktopText.FontWeight, DesktopText.FontStretch);
        var formatted = MeasureText(DesktopText.Text, typeface, DesktopText.FontSize, foreground);

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

        _activeWidth = contentWidth + horizontalPadding + borderSize;
        _activeHeight = contentHeight + verticalPadding + borderSize;

        var leftDesktops = _displayState?.LeftDesktops ?? Array.Empty<VirtualDesktopInfo>();
        var rightDesktops = _displayState?.RightDesktops ?? Array.Empty<VirtualDesktopInfo>();

        var neighborFontSize = DesktopText.FontSize * VirtualDesktopNeighborLayout.NeighborFontScale;
        var neighborPaddingH = OverlaySettings.BasePaddingHorizontal * scale * VirtualDesktopNeighborLayout.NeighborPaddingScale;
        var neighborPaddingV = OverlaySettings.BasePaddingVertical * scale * VirtualDesktopNeighborLayout.NeighborPaddingScale;
        var neighborCorner = BaseCornerRadius * scale * 0.75;
        var neighborMaxContent = VirtualDesktopNeighborLayout.NeighborMaxContentWidth * scale;
        var neighborBorderSize = settings.VirtualDesktopOverlayShowOutline ? OutlineBorderSize : 0;
        var neighborChromeH = (neighborPaddingH * 2) + neighborBorderSize;
        var neighborTypeface = new Typeface(
            DesktopText.FontFamily, DesktopText.FontStyle, FontWeights.SemiBold, DesktopText.FontStretch);

        var pixelsPerDip = GetPixelsPerDip();
        var leftWidths = MeasureNeighborWidths(leftDesktops, neighborTypeface, neighborFontSize, foreground, neighborMaxContent, neighborChromeH, pixelsPerDip);
        var rightWidths = MeasureNeighborWidths(rightDesktops, neighborTypeface, neighborFontSize, foreground, neighborMaxContent, neighborChromeH, pixelsPerDip);

        var activeOrigin = ResolveActiveOrigin(settings.VirtualDesktopOverlayPositionPreset, _activeWidth, _activeHeight);
        var (boundsLeft, boundsRight) = ResolveHorizontalBounds(settings.VirtualDesktopOverlayPositionPreset, workArea);
        var (leftBudget, rightBudget) = VirtualDesktopNeighborLayout.ComputeHorizontalBudgets(
            boundsLeft,
            boundsRight,
            activeOrigin.X,
            _activeWidth);

        var layout = VirtualDesktopNeighborLayout.SelectVisible(
            leftDesktops,
            leftWidths,
            rightDesktops,
            rightWidths,
            leftBudget,
            rightBudget);

        RebuildNeighborStrip(
            LeftStrip,
            layout.VisibleLeft,
            leftDesktops,
            leftWidths,
            settings,
            neighborFontSize,
            neighborPaddingH,
            neighborPaddingV,
            neighborCorner,
            neighborMaxContent,
            foreground,
            placeGapAfter: true);

        RebuildNeighborStrip(
            RightStrip,
            layout.VisibleRight,
            rightDesktops,
            rightWidths,
            settings,
            neighborFontSize,
            neighborPaddingH,
            neighborPaddingV,
            neighborCorner,
            neighborMaxContent,
            foreground,
            placeGapAfter: false);

        RootBorder.Margin = new Thickness(layout.LeftActiveGap, 0, layout.ActiveRightGap, 0);
        _activeOffsetX = layout.ActiveOffsetX;
        _activeOffsetY = 0;

        Width = layout.RenderedLeftStripWidth
            + layout.LeftActiveGap
            + _activeWidth
            + layout.ActiveRightGap
            + layout.RenderedRightStripWidth;
        Height = _activeHeight;
    }

    private static List<double> MeasureNeighborWidths(
        IReadOnlyList<VirtualDesktopInfo> desktops,
        Typeface typeface,
        double fontSize,
        System.Windows.Media.Brush foreground,
        double maxContentWidth,
        double chromeHorizontal,
        double pixelsPerDip)
    {
        var widths = new List<double>(desktops.Count);
        foreach (var desktop in desktops)
        {
            var formatted = new FormattedText(
                desktop.DisplayName,
                CultureInfo.CurrentUICulture,
                System.Windows.FlowDirection.LeftToRight,
                typeface,
                fontSize,
                foreground,
                pixelsPerDip);

            var contentWidth = Math.Ceiling(formatted.WidthIncludingTrailingWhitespace);
            if (contentWidth > maxContentWidth)
            {
                contentWidth = maxContentWidth;
            }

            widths.Add(contentWidth + chromeHorizontal);
        }

        return widths;
    }

    private void RebuildNeighborStrip(
        StackPanel strip,
        IReadOnlyList<VirtualDesktopInfo> visible,
        IReadOnlyList<VirtualDesktopInfo> allSide,
        IReadOnlyList<double> allWidths,
        AppSettings settings,
        double fontSize,
        double paddingH,
        double paddingV,
        double cornerRadius,
        double maxContentWidth,
        SolidColorBrush foreground,
        bool placeGapAfter)
    {
        strip.Children.Clear();

        var widthById = new Dictionary<Guid, double>(allSide.Count);
        for (var i = 0; i < allSide.Count; i++)
        {
            widthById[allSide[i].Id] = allWidths[i];
        }

        var backgroundAlpha = (byte)Math.Round(
            OverlaySettings.ClampOpacity(settings.VirtualDesktopOverlayOpacity)
            * VirtualDesktopNeighborLayout.NeighborOpacityFactor
            * 255);
        var foregroundAlpha = (byte)Math.Round(
            OverlaySettings.ClampForegroundOpacity(settings.VirtualDesktopOverlayForegroundOpacity)
            * VirtualDesktopNeighborLayout.NeighborOpacityFactor
            * 255);
        var borderBrush = settings.VirtualDesktopOverlayShowOutline
            ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x40, 255, 255, 255))
            : null;

        for (var i = 0; i < visible.Count; i++)
        {
            var desktop = visible[i];
            var label = new TextBlock
            {
                Text = desktop.DisplayName,
                FontSize = fontSize,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(
                    foregroundAlpha, foreground.Color.R, foreground.Color.G, foreground.Color.B)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = maxContentWidth,
                VerticalAlignment = VerticalAlignment.Center
            };

            var border = new Border
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(backgroundAlpha, 20, 24, 32)),
                BorderBrush = borderBrush,
                BorderThickness = settings.VirtualDesktopOverlayShowOutline ? new Thickness(1) : new Thickness(0),
                CornerRadius = new CornerRadius(cornerRadius),
                Padding = new Thickness(paddingH, paddingV, paddingH, paddingV),
                VerticalAlignment = VerticalAlignment.Center,
                Width = widthById.TryGetValue(desktop.Id, out var measuredWidth) ? measuredWidth : double.NaN,
                Child = label,
                IsHitTestVisible = false
            };

            if (placeGapAfter)
            {
                if (i < visible.Count - 1)
                {
                    border.Margin = new Thickness(0, 0, VirtualDesktopNeighborLayout.NeighborGap, 0);
                }
            }
            else if (i > 0)
            {
                border.Margin = new Thickness(VirtualDesktopNeighborLayout.NeighborGap, 0, 0, 0);
            }

            strip.Children.Add(border);
        }
    }

    private System.Windows.Point ResolveActiveOrigin(string? preset, double activeWidth, double activeHeight)
    {
        var workArea = SystemParameters.WorkArea;
        var normalized = preset?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "show-on-taskbar" => TaskbarOverlayPosition.ComputeActiveOrigin(
                workArea,
                SystemParameters.PrimaryScreenWidth,
                SystemParameters.PrimaryScreenHeight,
                activeWidth,
                activeHeight),
            "bottom-right" => new System.Windows.Point(
                workArea.Right - activeWidth - ScreenMargin,
                workArea.Bottom - activeHeight - ScreenMargin),
            "bottom-left" => new System.Windows.Point(
                workArea.Left + ScreenMargin,
                workArea.Bottom - activeHeight - ScreenMargin),
            "top-right" => new System.Windows.Point(
                workArea.Right - activeWidth - ScreenMargin,
                workArea.Top + ScreenMargin),
            _ => new System.Windows.Point(
                workArea.Left + ScreenMargin,
                workArea.Top + ScreenMargin)
        };
    }

    private static (double BoundsLeft, double BoundsRight) ResolveHorizontalBounds(string? preset, Rect workArea)
    {
        if (IsTaskbarPosition(preset))
        {
            return (workArea.Left + TaskbarEdgeReserve, workArea.Right - TaskbarEdgeReserve);
        }

        return (workArea.Left + ScreenMargin, workArea.Right - ScreenMargin);
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
        var normalized = preset?.Trim().ToLowerInvariant();

        if (normalized == "show-on-taskbar")
        {
            TaskbarOverlayPosition.Apply(this, _activeWidth, _activeHeight, _activeOffsetX, _activeOffsetY);
            return;
        }

        var activeOrigin = ResolveActiveOrigin(preset, _activeWidth, _activeHeight);
        Left = activeOrigin.X - _activeOffsetX;
        Top = activeOrigin.Y - _activeOffsetY;
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

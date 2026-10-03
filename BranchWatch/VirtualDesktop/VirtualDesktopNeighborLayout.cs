namespace BranchWatch;

public sealed record VirtualDesktopNeighborLayoutResult(
    IReadOnlyList<VirtualDesktopInfo> VisibleLeft,
    IReadOnlyList<VirtualDesktopInfo> VisibleRight,
    double RenderedLeftStripWidth,
    double RenderedRightStripWidth,
    double LeftActiveGap,
    double ActiveRightGap,
    double ActiveOffsetX);

public static class VirtualDesktopNeighborLayout
{
    public const double NeighborMaxContentWidth = 96;
    public const double NeighborGap = 8;
    public const double ActiveSideGap = 12;
    public const double NeighborFontScale = 0.48;
    public const double NeighborOpacityFactor = 0.5;
    public const double NeighborPaddingScale = 0.55;

    public static (double LeftBudget, double RightBudget) ComputeHorizontalBudgets(
        double boundsLeft,
        double boundsRight,
        double activeLeft,
        double activeWidth)
    {
        var leftBudget = Math.Max(0, activeLeft - boundsLeft);
        var rightBudget = Math.Max(0, boundsRight - (activeLeft + activeWidth));
        return (leftBudget, rightBudget);
    }

    public static VirtualDesktopNeighborLayoutResult SelectVisible(
        IReadOnlyList<VirtualDesktopInfo> leftDesktops,
        IReadOnlyList<double> leftWidths,
        IReadOnlyList<VirtualDesktopInfo> rightDesktops,
        IReadOnlyList<double> rightWidths,
        double leftBudget,
        double rightBudget,
        double neighborGap = NeighborGap,
        double activeSideGap = ActiveSideGap)
    {
        ArgumentNullException.ThrowIfNull(leftDesktops);
        ArgumentNullException.ThrowIfNull(leftWidths);
        ArgumentNullException.ThrowIfNull(rightDesktops);
        ArgumentNullException.ThrowIfNull(rightWidths);

        if (leftDesktops.Count != leftWidths.Count)
        {
            throw new ArgumentException("Left desktop and width counts must match.");
        }

        if (rightDesktops.Count != rightWidths.Count)
        {
            throw new ArgumentException("Right desktop and width counts must match.");
        }

        var visibleLeft = SelectNearestLast(leftDesktops, leftWidths, leftBudget, neighborGap, activeSideGap);
        var visibleRight = SelectNearestFirst(rightDesktops, rightWidths, rightBudget, neighborGap, activeSideGap);

        var leftStripWidth = SumStripWidth(visibleLeft.Widths, neighborGap);
        var rightStripWidth = SumStripWidth(visibleRight.Widths, neighborGap);
        var leftGap = visibleLeft.Desktops.Count > 0 ? activeSideGap : 0;
        var rightGap = visibleRight.Desktops.Count > 0 ? activeSideGap : 0;
        var activeOffsetX = leftStripWidth + leftGap;

        return new VirtualDesktopNeighborLayoutResult(
            visibleLeft.Desktops,
            visibleRight.Desktops,
            leftStripWidth,
            rightStripWidth,
            leftGap,
            rightGap,
            activeOffsetX);
    }

    private static (IReadOnlyList<VirtualDesktopInfo> Desktops, IReadOnlyList<double> Widths) SelectNearestLast(
        IReadOnlyList<VirtualDesktopInfo> desktops,
        IReadOnlyList<double> widths,
        double budget,
        double neighborGap,
        double activeSideGap)
    {
        var selected = new List<VirtualDesktopInfo>();
        var selectedWidths = new List<double>();
        var remaining = budget;

        for (var i = desktops.Count - 1; i >= 0; i--)
        {
            var cost = widths[i] + (selected.Count == 0 ? activeSideGap : neighborGap);
            if (cost > remaining)
            {
                break;
            }

            selected.Insert(0, desktops[i]);
            selectedWidths.Insert(0, widths[i]);
            remaining -= cost;
        }

        return (selected, selectedWidths);
    }

    private static (IReadOnlyList<VirtualDesktopInfo> Desktops, IReadOnlyList<double> Widths) SelectNearestFirst(
        IReadOnlyList<VirtualDesktopInfo> desktops,
        IReadOnlyList<double> widths,
        double budget,
        double neighborGap,
        double activeSideGap)
    {
        var selected = new List<VirtualDesktopInfo>();
        var selectedWidths = new List<double>();
        var remaining = budget;

        for (var i = 0; i < desktops.Count; i++)
        {
            var cost = widths[i] + (selected.Count == 0 ? activeSideGap : neighborGap);
            if (cost > remaining)
            {
                break;
            }

            selected.Add(desktops[i]);
            selectedWidths.Add(widths[i]);
            remaining -= cost;
        }

        return (selected, selectedWidths);
    }

    private static double SumStripWidth(IReadOnlyList<double> widths, double neighborGap)
    {
        if (widths.Count == 0)
        {
            return 0;
        }

        var total = 0.0;
        for (var i = 0; i < widths.Count; i++)
        {
            total += widths[i];
            if (i > 0)
            {
                total += neighborGap;
            }
        }

        return total;
    }
}

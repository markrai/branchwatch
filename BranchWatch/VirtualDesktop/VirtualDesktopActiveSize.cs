namespace BranchWatch;

public readonly record struct VirtualDesktopActiveSizeResult(
    double NaturalContentWidth,
    double ContentWidth,
    double ContentHeight,
    double ActiveWidth,
    double ActiveHeight,
    bool IsTruncated);

/// <summary>
/// Deterministic active-desktop chrome sizing, independent of neighbor composition
/// and prior WPF layout state.
/// </summary>
public static class VirtualDesktopActiveSize
{
    public static VirtualDesktopActiveSizeResult Measure(
        double naturalContentWidth,
        double contentHeight,
        double horizontalPaddingTotal,
        double verticalPaddingTotal,
        double borderSize,
        double maxContentWidth)
    {
        if (naturalContentWidth < 0 || contentHeight < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(naturalContentWidth), "Content dimensions must be non-negative.");
        }

        if (maxContentWidth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxContentWidth));
        }

        var truncated = naturalContentWidth > maxContentWidth;
        var contentWidth = truncated ? maxContentWidth : naturalContentWidth;

        return new VirtualDesktopActiveSizeResult(
            naturalContentWidth,
            contentWidth,
            contentHeight,
            contentWidth + horizontalPaddingTotal + borderSize,
            contentHeight + verticalPaddingTotal + borderSize,
            truncated);
    }
}

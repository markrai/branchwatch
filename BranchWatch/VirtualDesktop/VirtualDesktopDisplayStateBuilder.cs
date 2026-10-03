namespace BranchWatch;

public static class VirtualDesktopDisplayStateBuilder
{
    public static VirtualDesktopDisplayState Build(
        IReadOnlyList<VirtualDesktopInfo> orderedDesktops,
        Guid activeDesktopId)
    {
        ArgumentNullException.ThrowIfNull(orderedDesktops);
        if (orderedDesktops.Count == 0)
        {
            throw new ArgumentException("At least one desktop is required.", nameof(orderedDesktops));
        }

        var activeIndex = -1;
        for (var i = 0; i < orderedDesktops.Count; i++)
        {
            if (orderedDesktops[i].Id == activeDesktopId)
            {
                activeIndex = i;
                break;
            }
        }

        if (activeIndex < 0)
        {
            throw new ArgumentException(
                "Active desktop id was not found in the ordered desktop list.",
                nameof(activeDesktopId));
        }

        var left = activeIndex == 0
            ? Array.Empty<VirtualDesktopInfo>()
            : orderedDesktops.Take(activeIndex).ToArray();

        var right = activeIndex >= orderedDesktops.Count - 1
            ? Array.Empty<VirtualDesktopInfo>()
            : orderedDesktops.Skip(activeIndex + 1).ToArray();

        return new VirtualDesktopDisplayState(
            orderedDesktops[activeIndex],
            left,
            right,
            orderedDesktops.ToArray());
    }
}

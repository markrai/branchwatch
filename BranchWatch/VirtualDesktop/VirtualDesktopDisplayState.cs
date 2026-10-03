namespace BranchWatch;

public sealed record VirtualDesktopDisplayState(
    VirtualDesktopInfo ActiveDesktop,
    IReadOnlyList<VirtualDesktopInfo> LeftDesktops,
    IReadOnlyList<VirtualDesktopInfo> RightDesktops,
    IReadOnlyList<VirtualDesktopInfo> AllDesktops);

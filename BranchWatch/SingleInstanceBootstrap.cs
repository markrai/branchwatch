namespace BranchWatch;

internal static class SingleInstanceBootstrap
{
    public static BranchWatchSingleInstance? AcquiredInstance { get; set; }
}

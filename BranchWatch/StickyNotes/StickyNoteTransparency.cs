namespace BranchWatch;

internal static class StickyNoteTransparency
{
    internal const int MinLevel = 1;
    internal const int MaxLevel = 10;
    internal const int DefaultLevel = 3;

    private static readonly int[] OpacityPercent = [100, 98, 95, 90, 85, 75, 65, 55, 45, 35];

    internal static int ClampLevel(int level) => Math.Clamp(level, MinLevel, MaxLevel);

    internal static double OpacityForLevel(int level) =>
        OpacityPercent[ClampLevel(level) - MinLevel] / 100.0;
}

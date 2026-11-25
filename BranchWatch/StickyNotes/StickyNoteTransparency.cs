using System.Runtime.InteropServices;

namespace BranchWatch;

internal static class StickyNoteTransparency
{
    internal const int MinLevel = 1;
    internal const int MaxLevel = 10;
    internal const int DefaultLevel = 3;

    private const int GwlExStyle = -20;
    private const int WsExLayered = 0x00080000;
    private const uint LwaAlpha = 0x00000002;
    private static readonly int[] OpacityPercent = [100, 98, 95, 90, 85, 75, 65, 55, 45, 35];

    internal static int ClampLevel(int level) => Math.Clamp(level, MinLevel, MaxLevel);

    internal static byte AlphaForLevel(int level) =>
        (byte)Math.Round(OpacityPercent[ClampLevel(level) - MinLevel] * 255.0 / 100);

    internal static bool TryApply(IntPtr handle, int level)
    {
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        var style = GetWindowLong(handle, GwlExStyle);
        if (style == 0 && Marshal.GetLastPInvokeError() != 0)
        {
            return false;
        }

        var alpha = AlphaForLevel(level);
        if ((style & WsExLayered) == 0)
        {
            if (alpha == byte.MaxValue)
            {
                return true;
            }

            var previous = SetWindowLong(handle, GwlExStyle, style | WsExLayered);
            if (previous == 0 && Marshal.GetLastPInvokeError() != 0)
            {
                return false;
            }

            if (!SetLayeredWindowAttributes(handle, 0, alpha, LwaAlpha))
            {
                SetWindowLong(handle, GwlExStyle, style);
                return false;
            }

            return true;
        }

        return SetLayeredWindowAttributes(handle, 0, alpha, LwaAlpha);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr handle, int index, int value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(IntPtr handle, uint colorKey, byte alpha, uint flags);
}

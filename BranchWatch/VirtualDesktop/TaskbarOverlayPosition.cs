using System.Windows;

namespace BranchWatch;

internal static class TaskbarOverlayPosition
{
    public static void Apply(Window window)
    {
        var workArea = SystemParameters.WorkArea;
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;
        var width = window.Width;
        var height = window.Height;

        if (workArea.Bottom < screenHeight - 0.5)
        {
            var taskbarHeight = screenHeight - workArea.Bottom;
            window.Left = workArea.Left + (workArea.Width - width) / 2;
            window.Top = workArea.Bottom + (taskbarHeight - height) / 2;
            return;
        }

        if (workArea.Top > 0.5)
        {
            var taskbarHeight = workArea.Top;
            window.Left = workArea.Left + (workArea.Width - width) / 2;
            window.Top = (taskbarHeight - height) / 2;
            return;
        }

        if (workArea.Left > 0.5)
        {
            var taskbarWidth = workArea.Left;
            window.Left = (taskbarWidth - width) / 2;
            window.Top = workArea.Top + (workArea.Height - height) / 2;
            return;
        }

        if (workArea.Right < screenWidth - 0.5)
        {
            var taskbarWidth = screenWidth - workArea.Right;
            window.Left = workArea.Right + (taskbarWidth - width) / 2;
            window.Top = workArea.Top + (workArea.Height - height) / 2;
            return;
        }

        window.Left = workArea.Left + (workArea.Width - width) / 2;
        window.Top = workArea.Bottom - height;
    }
}

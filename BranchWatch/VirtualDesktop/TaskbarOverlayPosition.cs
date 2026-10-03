using System.Windows;

namespace BranchWatch;

internal static class TaskbarOverlayPosition
{
    public static void Apply(
        Window window,
        double activeWidth,
        double activeHeight,
        double activeOffsetX,
        double activeOffsetY)
    {
        var origin = ComputeOrigin(
            SystemParameters.WorkArea,
            SystemParameters.PrimaryScreenWidth,
            SystemParameters.PrimaryScreenHeight,
            activeWidth,
            activeHeight,
            activeOffsetX,
            activeOffsetY);

        window.Left = origin.X;
        window.Top = origin.Y;
    }

    public static void Apply(Window window)
    {
        Apply(window, window.Width, window.Height, activeOffsetX: 0, activeOffsetY: 0);
    }

    public static System.Windows.Point ComputeOrigin(
        Rect workArea,
        double screenWidth,
        double screenHeight,
        double activeWidth,
        double activeHeight,
        double activeOffsetX,
        double activeOffsetY)
    {
        var activeOrigin = ComputeActiveOrigin(workArea, screenWidth, screenHeight, activeWidth, activeHeight);
        return new System.Windows.Point(activeOrigin.X - activeOffsetX, activeOrigin.Y - activeOffsetY);
    }

    public static System.Windows.Point ComputeActiveOrigin(
        Rect workArea,
        double screenWidth,
        double screenHeight,
        double activeWidth,
        double activeHeight)
    {
        if (workArea.Bottom < screenHeight - 0.5)
        {
            var taskbarHeight = screenHeight - workArea.Bottom;
            return new System.Windows.Point(
                workArea.Left + (workArea.Width - activeWidth) / 2,
                workArea.Bottom + (taskbarHeight - activeHeight) / 2);
        }

        if (workArea.Top > 0.5)
        {
            var taskbarHeight = workArea.Top;
            return new System.Windows.Point(
                workArea.Left + (workArea.Width - activeWidth) / 2,
                (taskbarHeight - activeHeight) / 2);
        }

        if (workArea.Left > 0.5)
        {
            var taskbarWidth = workArea.Left;
            return new System.Windows.Point(
                (taskbarWidth - activeWidth) / 2,
                workArea.Top + (workArea.Height - activeHeight) / 2);
        }

        if (workArea.Right < screenWidth - 0.5)
        {
            var taskbarWidth = screenWidth - workArea.Right;
            return new System.Windows.Point(
                workArea.Right + (taskbarWidth - activeWidth) / 2,
                workArea.Top + (workArea.Height - activeHeight) / 2);
        }

        return new System.Windows.Point(
            workArea.Left + (workArea.Width - activeWidth) / 2,
            workArea.Bottom - activeHeight);
    }
}

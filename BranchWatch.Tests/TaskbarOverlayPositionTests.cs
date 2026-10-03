using System.Windows;

namespace BranchWatch.Tests;

[TestClass]
public sealed class TaskbarOverlayPositionTests
{
    private static readonly Rect BottomTaskbarWorkArea = new(0, 0, 1920, 1040);
    private const double ScreenWidth = 1920;
    private const double ScreenHeight = 1080;
    private const double ActiveWidth = 220;
    private const double ActiveHeight = 52;

    [TestMethod]
    public void ComputeOrigin_PreservesActiveCenterX_AcrossNeighborConfigurations()
    {
        var baselineActiveLeft = TaskbarOverlayPosition.ComputeActiveOrigin(
            BottomTaskbarWorkArea,
            ScreenWidth,
            ScreenHeight,
            ActiveWidth,
            ActiveHeight).X;
        var baselineActiveCenterX = baselineActiveLeft + (ActiveWidth / 2);

        var cases = new (string Name, double ActiveOffsetX)[]
        {
            ("zero-left-zero-right", 0),
            ("three-left-zero-right", (30 + 8 + 40 + 8 + 50) + 12),
            ("zero-left-three-right", 0),
            ("three-left-three-right", (25 + 8 + 35 + 8 + 45) + 12),
            ("unequal-neighbor-widths", (10 + 8 + 90) + 12)
        };

        foreach (var (_, activeOffsetX) in cases)
        {
            var origin = TaskbarOverlayPosition.ComputeOrigin(
                BottomTaskbarWorkArea,
                ScreenWidth,
                ScreenHeight,
                ActiveWidth,
                ActiveHeight,
                activeOffsetX,
                activeOffsetY: 0);

            var activeCenterX = origin.X + activeOffsetX + (ActiveWidth / 2);
            Assert.AreEqual(baselineActiveCenterX, activeCenterX, 0.0001);
        }
    }

    [TestMethod]
    public void ComputeOrigin_WindowLeftPlusActiveOffset_EqualsOldActiveLeft()
    {
        const double activeOffsetX = 96;
        var oldActiveLeft = TaskbarOverlayPosition.ComputeActiveOrigin(
            BottomTaskbarWorkArea,
            ScreenWidth,
            ScreenHeight,
            ActiveWidth,
            ActiveHeight).X;

        var origin = TaskbarOverlayPosition.ComputeOrigin(
            BottomTaskbarWorkArea,
            ScreenWidth,
            ScreenHeight,
            ActiveWidth,
            ActiveHeight,
            activeOffsetX,
            activeOffsetY: 0);

        Assert.AreEqual(oldActiveLeft, origin.X + activeOffsetX, 0.0001);
    }
}

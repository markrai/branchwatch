namespace BranchWatch.Tests;

[TestClass]
public sealed class VirtualDesktopNeighborLayoutTests
{
    private static readonly Guid IdA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid IdB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid IdD = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid IdE = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    [TestMethod]
    public void SelectVisible_PrefersNearestNeighbors_WhenBudgetIsTight()
    {
        var left = new[] { Desktop(IdA, "A"), Desktop(IdB, "B") };
        var right = new[] { Desktop(IdD, "D"), Desktop(IdE, "E") };
        var leftWidths = new[] { 40.0, 40.0 };
        var rightWidths = new[] { 40.0, 40.0 };

        // One neighbor + side gap fits; two do not.
        var budget = 40 + VirtualDesktopNeighborLayout.ActiveSideGap;

        var layout = VirtualDesktopNeighborLayout.SelectVisible(
            left,
            leftWidths,
            right,
            rightWidths,
            leftBudget: budget,
            rightBudget: budget);

        CollectionAssert.AreEqual(new[] { "B" }, layout.VisibleLeft.Select(d => d.DisplayName).ToArray());
        CollectionAssert.AreEqual(new[] { "D" }, layout.VisibleRight.Select(d => d.DisplayName).ToArray());
    }

    [TestMethod]
    public void SelectVisible_NoLeftNeighbors_ActiveOffsetXIsZero()
    {
        var right = new[] { Desktop(IdD, "D") };
        var layout = VirtualDesktopNeighborLayout.SelectVisible(
            Array.Empty<VirtualDesktopInfo>(),
            Array.Empty<double>(),
            right,
            [50],
            leftBudget: 200,
            rightBudget: 200);

        Assert.AreEqual(0, layout.ActiveOffsetX);
        Assert.AreEqual(0, layout.LeftActiveGap);
        Assert.AreEqual(VirtualDesktopNeighborLayout.ActiveSideGap, layout.ActiveRightGap);
    }

    [TestMethod]
    public void SelectVisible_ActiveOffsetX_IncludesStripAndGap()
    {
        var left = new[] { Desktop(IdA, "A"), Desktop(IdB, "B") };
        var layout = VirtualDesktopNeighborLayout.SelectVisible(
            left,
            [30, 40],
            Array.Empty<VirtualDesktopInfo>(),
            Array.Empty<double>(),
            leftBudget: 500,
            rightBudget: 500);

        var expectedStrip = 30 + VirtualDesktopNeighborLayout.NeighborGap + 40;
        Assert.AreEqual(expectedStrip, layout.RenderedLeftStripWidth);
        Assert.AreEqual(VirtualDesktopNeighborLayout.ActiveSideGap, layout.LeftActiveGap);
        Assert.AreEqual(expectedStrip + VirtualDesktopNeighborLayout.ActiveSideGap, layout.ActiveOffsetX);
    }

    [TestMethod]
    public void SelectVisible_IndependentBudgets_CanClipSidesUnequally()
    {
        var left = new[] { Desktop(IdA, "A"), Desktop(IdB, "B") };
        var right = new[] { Desktop(IdD, "D"), Desktop(IdE, "E") };

        var layout = VirtualDesktopNeighborLayout.SelectVisible(
            left,
            [40, 40],
            right,
            [40, 40],
            leftBudget: 40 + VirtualDesktopNeighborLayout.ActiveSideGap,
            rightBudget: 500);

        CollectionAssert.AreEqual(new[] { "B" }, layout.VisibleLeft.Select(d => d.DisplayName).ToArray());
        CollectionAssert.AreEqual(new[] { "D", "E" }, layout.VisibleRight.Select(d => d.DisplayName).ToArray());
    }

    [TestMethod]
    public void ComputeHorizontalBudgets_UsesIndependentSides()
    {
        var (left, right) = VirtualDesktopNeighborLayout.ComputeHorizontalBudgets(
            boundsLeft: 0,
            boundsRight: 1000,
            activeLeft: 100,
            activeWidth: 200);

        Assert.AreEqual(100, left);
        Assert.AreEqual(700, right);
    }

    [TestMethod]
    public void SelectVisible_LongNeighborWidths_StillRespectBudgetAndNearestPreference()
    {
        var left = new[] { Desktop(IdA, "A"), Desktop(IdB, "B") };
        var longWidth = VirtualDesktopNeighborLayout.NeighborMaxContentWidth + 40;
        var shortWidth = 40.0;
        var budget = shortWidth + VirtualDesktopNeighborLayout.ActiveSideGap;

        var layout = VirtualDesktopNeighborLayout.SelectVisible(
            left,
            [longWidth, shortWidth],
            Array.Empty<VirtualDesktopInfo>(),
            Array.Empty<double>(),
            leftBudget: budget,
            rightBudget: 0);

        CollectionAssert.AreEqual(new[] { "B" }, layout.VisibleLeft.Select(d => d.DisplayName).ToArray());
    }

    private static VirtualDesktopInfo Desktop(Guid id, string name) => new(id, name);
}

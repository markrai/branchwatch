using System.Windows;

namespace BranchWatch.Tests;

[TestClass]
public sealed class StickyNoteResizeGeometryTests
{
    private static readonly StickyNoteResizeBounds Start = new(-400, 100, 300, 260);

    [TestMethod]
    public void Shrink_AnchorsTopRightAndDisallowsGrowth()
    {
        var smaller = StickyNoteResizeGeometry.Shrink(Start, Start, 30, -40, 220, 150);
        Assert.AreEqual(new StickyNoteResizeBounds(-370, 100, 270, 220), smaller);
        Assert.AreEqual(smaller, StickyNoteResizeGeometry.Shrink(Start, smaller, -30, 40, 220, 150));
        Assert.AreEqual(smaller, StickyNoteResizeGeometry.Shrink(Start, smaller, 10, -10, 220, 150));
        Assert.AreEqual(smaller, StickyNoteResizeGeometry.Shrink(smaller, smaller, -30, 40, 220, 150));
    }

    [TestMethod]
    public void Shrink_ClampsEachAxisIndependentlyAtMinimum()
    {
        Assert.AreEqual(new StickyNoteResizeBounds(-320, 100, 220, 150),
            StickyNoteResizeGeometry.Shrink(Start, Start, 1000, -1000, 220, 150));
        Assert.AreEqual(new StickyNoteResizeBounds(-350, 100, 250, 260),
            StickyNoteResizeGeometry.Shrink(Start, Start, 50, 40, 220, 150));
        Assert.AreEqual(new StickyNoteResizeBounds(-400, 100, 300, 200),
            StickyNoteResizeGeometry.Shrink(Start, Start, -40, -60, 220, 150));
    }

    [TestMethod]
    public void PassiveBodyClip_LeavesOnlyBottomLeftGripCutout()
    {
        var clip = StickyNoteResizeGeometry.PassiveBodyClip(298, 222);
        Assert.IsFalse(clip.FillContains(new Point(5, 217)));
        Assert.IsTrue(clip.FillContains(new Point(20, 217)));
        Assert.IsTrue(clip.FillContains(new Point(5, 200)));
        Assert.IsTrue(clip.FillContains(new Point(250, 217)));
    }
}

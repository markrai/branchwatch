using System.Windows;

namespace BranchWatch.Tests;

[TestClass]
public sealed class StickyNoteResizeGeometryTests
{
    private static readonly StickyNoteResizeBounds Start = new(-400, 100, 300, 260);

    [TestMethod]
    public void Resize_AnchorsTopRightAndRestoresOriginalSizeAcrossDrags()
    {
        var smaller = StickyNoteResizeGeometry.Resize(Start, 30, -40, 220, 150, 300, 260);
        Assert.AreEqual(new StickyNoteResizeBounds(-370, 100, 270, 220), smaller);
        Assert.AreEqual(Start, StickyNoteResizeGeometry.Resize(Start, 0, 0, 220, 150, 300, 260));
        Assert.AreEqual(Start, StickyNoteResizeGeometry.Resize(smaller, -100, 100, 220, 150, 300, 260));
        Assert.AreEqual(Start, StickyNoteResizeGeometry.Resize(Start, -100, 100, 220, 150, 300, 260));
        Assert.AreEqual(new StickyNoteResizeBounds(-380, 100, 280, 230),
            StickyNoteResizeGeometry.Resize(smaller, -10, 10, 220, 150, 300, 260));
    }

    [TestMethod]
    public void Resize_ClampsEachAxisIndependently()
    {
        Assert.AreEqual(new StickyNoteResizeBounds(-320, 100, 220, 150),
            StickyNoteResizeGeometry.Resize(Start, 1000, -1000, 220, 150, 300, 260));
        Assert.AreEqual(new StickyNoteResizeBounds(-350, 100, 250, 260),
            StickyNoteResizeGeometry.Resize(Start, 50, 40, 220, 150, 300, 260));
        Assert.AreEqual(new StickyNoteResizeBounds(-400, 100, 300, 200),
            StickyNoteResizeGeometry.Resize(Start, -40, -60, 220, 150, 300, 260));
        Assert.AreEqual(new StickyNoteResizeBounds(-520, 100, 420, 350),
            StickyNoteResizeGeometry.Resize(Start, -1000, 1000, 220, 150, 420, 350));
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

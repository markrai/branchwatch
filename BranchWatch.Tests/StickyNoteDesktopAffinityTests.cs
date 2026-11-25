namespace BranchWatch.Tests;

[TestClass]
public sealed class StickyNoteDesktopAffinityTests
{
    [TestMethod]
    public void Verify_AllowsOwnedAuxiliaryWindowWithoutSeparateDesktopIdentity()
    {
        var desktop = Guid.NewGuid();
        Assert.IsTrue(StickyNoteDesktopAffinity.Verify(true, desktop, true, null, false, out _));
        Assert.IsTrue(StickyNoteDesktopAffinity.Verify(true, desktop, true, desktop, true, out _));
    }

    [TestMethod]
    public void Verify_RejectsUnownedOrExplicitlyMismatchedWindows()
    {
        var desktop = Guid.NewGuid();
        Assert.IsFalse(StickyNoteDesktopAffinity.Verify(false, desktop, true, null, false, out _));
        Assert.IsFalse(StickyNoteDesktopAffinity.Verify(true, desktop, false, null, false, out _));
        Assert.IsFalse(StickyNoteDesktopAffinity.Verify(true, null, false, null, false, out _));
        Assert.IsFalse(StickyNoteDesktopAffinity.Verify(true, desktop, true, Guid.NewGuid(), true, out _));
        Assert.IsFalse(StickyNoteDesktopAffinity.Verify(true, desktop, true, desktop, false, out _));
    }
}

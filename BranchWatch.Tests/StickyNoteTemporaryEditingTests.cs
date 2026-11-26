using System.Diagnostics;

namespace BranchWatch.Tests;

[TestClass]
public sealed class StickyNoteTemporaryEditingTests
{
    [TestMethod]
    public void Preview_CanBeHeldOnlyWithinFiveSeconds()
    {
        var state = new StickyNoteTemporaryEditing();
        var start = Stopwatch.GetTimestamp();
        state.Begin(start);
        Assert.IsTrue(state.IsPreview);
        Assert.IsTrue(state.TryHold(start + Stopwatch.Frequency * 4));
        Assert.IsTrue(state.IsHeld);
        Assert.IsFalse(state.IsPreview);
        Assert.IsFalse(state.TryHold(start + Stopwatch.Frequency * 10));
        Assert.IsTrue(state.IsHeld);
        Assert.IsTrue(state.End());
        Assert.IsFalse(state.IsActive);

        state.Begin(start);
        Assert.IsFalse(state.TryHold(start + Stopwatch.Frequency * 5));
        Assert.IsTrue(state.End());
        Assert.IsFalse(state.End());
    }
}

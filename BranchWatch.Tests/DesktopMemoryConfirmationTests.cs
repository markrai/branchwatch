namespace BranchWatch.Tests;

[TestClass]
public sealed class DesktopMemoryConfirmationTests
{
    [TestMethod]
    public void Approval_RequiresEligibleStateBeforeAndAfterPrompt()
    {
        var confirmation = new DesktopMemoryConfirmation();
        var eligible = false;
        var prompted = 0;
        Assert.IsFalse(confirmation.Confirm(() => eligible, () => { prompted++; return true; }));
        Assert.AreEqual(0, prompted);
        eligible = true;
        Assert.IsFalse(confirmation.Confirm(() => eligible, () => { eligible = false; return true; }));
        eligible = true;
        Assert.IsTrue(confirmation.Confirm(() => eligible, () => true));
        Assert.IsFalse(confirmation.Confirm(() => eligible, () => false));
    }

    [TestMethod]
    public void Invalidation_RejectsApprovalEvenWhenEligibilityReturns()
    {
        var confirmation = new DesktopMemoryConfirmation();
        Assert.IsFalse(confirmation.Confirm(() => true, () =>
        {
            confirmation.Invalidate();
            return true;
        }));
        Assert.IsTrue(confirmation.Confirm(() => true, () => true));
    }

    [TestMethod]
    public void NestedPrompt_IsRejectedWithoutConsumingOuterApproval()
    {
        var confirmation = new DesktopMemoryConfirmation();
        var prompted = false;
        Assert.IsTrue(confirmation.Confirm(() => true, () =>
        {
            Assert.IsFalse(confirmation.Confirm(() => true, () => { prompted = true; return true; }));
            return true;
        }));
        Assert.IsFalse(prompted);
    }

    [TestMethod]
    public void FailedPrompt_DoesNotBlockFutureConfirmations()
    {
        var confirmation = new DesktopMemoryConfirmation();
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            confirmation.Confirm(() => true, () => throw new InvalidOperationException()));
        Assert.IsTrue(confirmation.Confirm(() => true, () => true));
    }
}

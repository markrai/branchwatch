namespace BranchWatch.Tests;

[TestClass]
public sealed class VirtualDesktopMonitorEqualityTests
{
    private static readonly Guid IdA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid IdB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid IdC = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [TestMethod]
    public void AreStructurallyEqual_True_ForFreshListsWithSameContents()
    {
        var left = BuildState(IdB, [("A", IdA), ("B", IdB), ("C", IdC)]);
        var right = BuildState(IdB, [("A", IdA), ("B", IdB), ("C", IdC)]);

        Assert.IsTrue(VirtualDesktopMonitor.AreStructurallyEqual(left, right));
    }

    [TestMethod]
    public void AreStructurallyEqual_False_WhenActiveChanges()
    {
        var left = BuildState(IdB, [("A", IdA), ("B", IdB), ("C", IdC)]);
        var right = BuildState(IdC, [("A", IdA), ("B", IdB), ("C", IdC)]);

        Assert.IsFalse(VirtualDesktopMonitor.AreStructurallyEqual(left, right));
    }

    [TestMethod]
    public void AreStructurallyEqual_False_WhenDesktopRenamed()
    {
        var left = BuildState(IdB, [("A", IdA), ("B", IdB), ("C", IdC)]);
        var right = BuildState(IdB, [("A", IdA), ("B", IdB), ("Renamed", IdC)]);

        Assert.IsFalse(VirtualDesktopMonitor.AreStructurallyEqual(left, right));
    }

    [TestMethod]
    public void AreStructurallyEqual_False_WhenDesktopDeleted()
    {
        var left = BuildState(IdB, [("A", IdA), ("B", IdB), ("C", IdC)]);
        var right = BuildState(IdB, [("A", IdA), ("B", IdB)]);

        Assert.IsFalse(VirtualDesktopMonitor.AreStructurallyEqual(left, right));
    }

    [TestMethod]
    public void AreStructurallyEqual_False_WhenReordered()
    {
        var left = BuildState(IdB, [("A", IdA), ("B", IdB), ("C", IdC)]);
        var right = BuildState(IdB, [("C", IdC), ("B", IdB), ("A", IdA)]);

        Assert.IsFalse(VirtualDesktopMonitor.AreStructurallyEqual(left, right));
    }

    private static VirtualDesktopDisplayState BuildState(
        Guid activeId,
        (string Name, Guid Id)[] ordered)
    {
        var desktops = ordered.Select(item => new VirtualDesktopInfo(item.Id, item.Name)).ToArray();
        return VirtualDesktopDisplayStateBuilder.Build(desktops, activeId);
    }
}

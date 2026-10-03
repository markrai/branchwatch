namespace BranchWatch.Tests;

[TestClass]
public sealed class VirtualDesktopDisplayStateBuilderTests
{
    private static readonly Guid IdA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid IdB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid IdC = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid IdD = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid IdE = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    [TestMethod]
    public void Build_OneDesktop_HasNoNeighbors()
    {
        var only = Desktop(IdA, "Blue");

        var state = VirtualDesktopDisplayStateBuilder.Build([only], IdA);

        Assert.AreEqual(only, state.ActiveDesktop);
        Assert.IsEmpty(state.LeftDesktops);
        Assert.IsEmpty(state.RightDesktops);
        Assert.HasCount(1, state.AllDesktops);
    }

    [TestMethod]
    public void Build_MiddleActive_SplitsLeftAndRightInOrder()
    {
        var desktops = new[]
        {
            Desktop(IdA, "A"),
            Desktop(IdB, "B"),
            Desktop(IdC, "C"),
            Desktop(IdD, "D"),
            Desktop(IdE, "E")
        };

        var state = VirtualDesktopDisplayStateBuilder.Build(desktops, IdC);

        CollectionAssert.AreEqual(new[] { "A", "B" }, state.LeftDesktops.Select(d => d.DisplayName).ToArray());
        Assert.AreEqual("C", state.ActiveDesktop.DisplayName);
        CollectionAssert.AreEqual(new[] { "D", "E" }, state.RightDesktops.Select(d => d.DisplayName).ToArray());
    }

    [TestMethod]
    public void Build_FirstActive_HasOnlyRightNeighbors()
    {
        var desktops = new[]
        {
            Desktop(IdA, "Blue"),
            Desktop(IdB, "Green"),
            Desktop(IdC, "Red")
        };

        var state = VirtualDesktopDisplayStateBuilder.Build(desktops, IdA);

        Assert.IsEmpty(state.LeftDesktops);
        CollectionAssert.AreEqual(new[] { "Green", "Red" }, state.RightDesktops.Select(d => d.DisplayName).ToArray());
    }

    [TestMethod]
    public void Build_LastActive_HasOnlyLeftNeighbors()
    {
        var desktops = new[]
        {
            Desktop(IdA, "Red"),
            Desktop(IdB, "Green"),
            Desktop(IdC, "Blue")
        };

        var state = VirtualDesktopDisplayStateBuilder.Build(desktops, IdC);

        CollectionAssert.AreEqual(new[] { "Red", "Green" }, state.LeftDesktops.Select(d => d.DisplayName).ToArray());
        Assert.IsEmpty(state.RightDesktops);
    }

    [TestMethod]
    public void Build_ActiveChange_ResplitsNeighbors()
    {
        var desktops = new[]
        {
            Desktop(IdA, "A"),
            Desktop(IdB, "B"),
            Desktop(IdC, "C")
        };

        var onB = VirtualDesktopDisplayStateBuilder.Build(desktops, IdB);
        var onA = VirtualDesktopDisplayStateBuilder.Build(desktops, IdA);

        Assert.AreEqual("B", onB.ActiveDesktop.DisplayName);
        CollectionAssert.AreEqual(new[] { "A" }, onB.LeftDesktops.Select(d => d.DisplayName).ToArray());
        CollectionAssert.AreEqual(new[] { "C" }, onB.RightDesktops.Select(d => d.DisplayName).ToArray());

        Assert.AreEqual("A", onA.ActiveDesktop.DisplayName);
        Assert.IsEmpty(onA.LeftDesktops);
        CollectionAssert.AreEqual(new[] { "B", "C" }, onA.RightDesktops.Select(d => d.DisplayName).ToArray());
    }

    [TestMethod]
    public void Build_Deletion_RemovesDesktopFromNeighbors()
    {
        var before = new[]
        {
            Desktop(IdA, "A"),
            Desktop(IdB, "B"),
            Desktop(IdC, "C"),
            Desktop(IdD, "D")
        };
        var afterDelete = new[]
        {
            Desktop(IdA, "A"),
            Desktop(IdC, "C"),
            Desktop(IdD, "D")
        };

        var state = VirtualDesktopDisplayStateBuilder.Build(afterDelete, IdC);

        CollectionAssert.AreEqual(new[] { "A" }, state.LeftDesktops.Select(d => d.DisplayName).ToArray());
        CollectionAssert.AreEqual(new[] { "D" }, state.RightDesktops.Select(d => d.DisplayName).ToArray());
        Assert.IsFalse(state.AllDesktops.Any(d => d.Id == IdB));
        Assert.HasCount(4, before);
    }

    [TestMethod]
    public void Build_Rename_UsesUpdatedDisplayName()
    {
        var desktops = new[]
        {
            Desktop(IdA, "Red"),
            Desktop(IdB, "Renamed"),
            Desktop(IdC, "Blue")
        };

        var state = VirtualDesktopDisplayStateBuilder.Build(desktops, IdC);

        Assert.AreEqual("Renamed", state.LeftDesktops[1].DisplayName);
    }

    private static VirtualDesktopInfo Desktop(Guid id, string name) => new(id, name);
}

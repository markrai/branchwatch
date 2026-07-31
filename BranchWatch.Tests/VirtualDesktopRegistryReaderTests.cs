namespace BranchWatch.Tests;

[TestClass]
public sealed class VirtualDesktopRegistryReaderTests
{
    [TestMethod]
    public void ParseDesktopIds_ReadsGuidsInSixteenByteChunks()
    {
        var id1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var id2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var bytes = id1.ToByteArray().Concat(id2.ToByteArray()).ToArray();

        var ids = VirtualDesktopRegistryReader.ParseDesktopIds(bytes);

        Assert.HasCount(2, ids);
        Assert.AreEqual(id1, ids[0]);
        Assert.AreEqual(id2, ids[1]);
    }

    [TestMethod]
    public void ParseDesktopIds_IgnoresTrailingPartialGuidBytes()
    {
        var id = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var bytes = id.ToByteArray().Concat(new byte[] { 1, 2, 3 }).ToArray();

        var ids = VirtualDesktopRegistryReader.ParseDesktopIds(bytes);

        Assert.HasCount(1, ids);
        Assert.AreEqual(id, ids[0]);
    }

    [TestMethod]
    public void ParseGuidValue_ReadsSixteenByteArray()
    {
        var id = Guid.Parse("44444444-4444-4444-4444-444444444444");

        var parsed = VirtualDesktopRegistryReader.ParseGuidValue(id.ToByteArray());

        Assert.AreEqual(id, parsed);
    }

    [TestMethod]
    public void ParseGuidValue_ReadsGuidString()
    {
        var id = Guid.Parse("55555555-5555-5555-5555-555555555555");

        var parsed = VirtualDesktopRegistryReader.ParseGuidValue(id.ToString("B"));

        Assert.AreEqual(id, parsed);
    }

    [TestMethod]
    public void SelectCurrentDesktopId_PrefersPrimaryCurrentWhenPresent()
    {
        var id1 = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var id2 = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var ids = new[] { id1, id2 };

        var selected = VirtualDesktopRegistryReader.SelectCurrentDesktopId(id2, id1, ids);

        Assert.AreEqual(id2, selected);
    }

    [TestMethod]
    public void SelectCurrentDesktopId_UsesSessionCurrentWhenPrimaryMissing()
    {
        var id1 = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var id2 = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var ids = new[] { id1, id2 };

        var selected = VirtualDesktopRegistryReader.SelectCurrentDesktopId(null, id2, ids);

        Assert.AreEqual(id2, selected);
    }

    [TestMethod]
    public void SelectCurrentDesktopId_FallsBackToFirstDesktop()
    {
        var id1 = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        var id2 = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var ids = new[] { id1, id2 };

        var selected = VirtualDesktopRegistryReader.SelectCurrentDesktopId(null, null, ids);

        Assert.AreEqual(id1, selected);
    }

    [TestMethod]
    public void ResolveDisplayName_UsesRegistryNameWhenPresent()
    {
        var name = VirtualDesktopRegistryReader.ResolveDisplayName(2, "  Coding  ");

        Assert.AreEqual("Coding", name);
    }

    [TestMethod]
    public void ResolveDisplayName_FallsBackToDefaultNumber()
    {
        var name = VirtualDesktopRegistryReader.ResolveDisplayName(0, null);

        Assert.AreEqual("Desktop 1", name);
    }
}

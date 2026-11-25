using System.IO;
using System.Resources;

namespace BranchWatch.Tests;

[TestClass]
public sealed class ApplicationIconTests
{
    [TestMethod]
    public void EmbeddedIconContainsTrayAndHighResolutionFrames()
    {
        var resources = new ResourceManager("BranchWatch.g", typeof(TrayService).Assembly);
        using var stream = resources.GetStream("assets/branchwatch.ico");
        Assert.IsNotNull(stream);
        using var reader = new BinaryReader(stream!);

        Assert.AreEqual((ushort)0, reader.ReadUInt16());
        Assert.AreEqual((ushort)1, reader.ReadUInt16());
        var count = reader.ReadUInt16();
        var sizes = new List<int>();

        for (var i = 0; i < count; i++)
        {
            var width = reader.ReadByte();
            var height = reader.ReadByte();
            Assert.AreEqual(width, height);
            sizes.Add(width == 0 ? 256 : width);
            reader.ReadBytes(6);
            var length = reader.ReadUInt32();
            var offset = reader.ReadUInt32();
            Assert.IsTrue(length > 0);
            Assert.IsTrue((long)offset + length <= stream!.Length);
        }

        CollectionAssert.AreEqual(new[] { 16, 24, 32, 48, 64, 256 }, sizes);
    }
}

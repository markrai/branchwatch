using System.Runtime.InteropServices;
using System.Text.Json;

namespace BranchWatch.Tests;

[TestClass]
public sealed class DesktopMemoryProbeTests
{
    private static ProbeDesktopMembership Membership(bool current = true) => new(Guid.NewGuid(), 0, current, 0);

    [TestMethod]
    public void Classification_SeparatesCandidatesFromUnverifiedAndAuxiliaryWindows()
    {
        Assert.AreEqual("candidate", ProbePolicy.Classify(true, false, false, false, false, false, Membership(), 0));
        Assert.AreEqual("candidate", ProbePolicy.Classify(true, false, false, false, false, false, Membership(false), 2));
        Assert.AreEqual("auxiliary", ProbePolicy.Classify(true, true, true, false, false, false, Membership(), 0));
        Assert.AreEqual("candidate", ProbePolicy.Classify(true, true, true, true, false, false, Membership(), 0));
        Assert.AreEqual("branchwatch", ProbePolicy.Classify(true, false, false, true, false, true, Membership(), 0));
        Assert.AreEqual("shell", ProbePolicy.Classify(true, false, false, false, true, false, Membership(), 0));
        Assert.AreEqual("desktop-unverified", ProbePolicy.Classify(true, false, false, false, false, false,
            new ProbeDesktopMembership(null, unchecked((int)0x80004005), null, -1), 0));
        Assert.AreEqual("cloak-unverified", ProbePolicy.Classify(true, false, false, false, false, false, Membership(), null));
        Assert.AreEqual("application-or-inherited-cloak", ProbePolicy.Classify(true, false, false, false, false, false, Membership(), 4));
    }

    [TestMethod]
    public void MonitorInterop_UsesExpectedWindowsStructureSizes()
    {
        Assert.AreEqual(104, Marshal.SizeOf<ProbeNative.MonitorInfo>());
        Assert.AreEqual(840, Marshal.SizeOf<ProbeNative.DisplayDevice>());
        Assert.AreEqual(220, Marshal.SizeOf<ProbeNative.DisplayMode>());
        Assert.AreEqual(44, Marshal.SizeOf<ProbeNative.WindowPlacement>());
        Assert.AreEqual(84, Marshal.OffsetOf<ProbeNative.DisplayMode>("Orientation").ToInt32());
        Assert.AreEqual(172, Marshal.OffsetOf<ProbeNative.DisplayMode>("Width").ToInt32());
        Assert.AreEqual<int?>(90, ProbePolicy.Rotation(1));
        Assert.AreEqual<int?>(270, ProbePolicy.Rotation(3));
        Assert.IsNull(ProbePolicy.Rotation(4));
    }

    [TestMethod]
    public void TopologyComparison_DetectsRotationAndIdentityChangesRegardlessOfOrder()
    {
        var a = new ProbeMonitor("A", new ProbeRect(-1080, 0, 0, 1920), new ProbeRect(-1080, 0, 0, 1880),
            false, 90, 1080, 1920, 60, 125, 0, [new ProbeDisplayDevice("device-a", 1, null)]);
        var b = a with { SourceName = "B", Primary = true, Devices = [new ProbeDisplayDevice("device-b", 1, null)] };
        Assert.IsTrue(ProbePolicy.SameTopology([a, b], [b, a with { Devices = a.Devices.ToArray() }]));
        Assert.IsFalse(ProbePolicy.SameTopology([a, b], [a with { RotationDegrees = 270 }, b]));
        Assert.IsFalse(ProbePolicy.SameTopology([a, b], [a with { Devices = b.Devices }, b]));
        Assert.IsFalse(ProbePolicy.SameTopology([a, b], [a]));
        Assert.AreEqual(1080L, a.Bounds.Width);
        Assert.AreEqual(1920L, a.Bounds.Height);
        Assert.IsTrue(JsonSerializer.Serialize(a).Contains("RotationDegrees", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ProbeCommand_RequiresExplicitOptInForPrivateIdentities()
    {
        Assert.IsTrue(DesktopMemoryProbeCommand.TryParse(["--desktop-memory-probe"], out var identities));
        Assert.IsFalse(identities);
        Assert.IsTrue(DesktopMemoryProbeCommand.TryParse(["--desktop-memory-probe", "--include-identities"], out identities));
        Assert.IsTrue(identities);
        Assert.IsFalse(DesktopMemoryProbeCommand.TryParse(["--desktop-memory-probe", "unknown"], out _));
        Assert.IsFalse(DesktopMemoryProbeCommand.TryParse(["C:\\dev"], out _));
        Assert.IsFalse(DesktopMemoryProbeCommand.TryParse([], out _));
    }
}

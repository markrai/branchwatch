namespace BranchWatch.Tests;

[TestClass]
public sealed class VirtualDesktopColorsTests
{
    [TestMethod]
    public void Resolve_UsesDesktopIdentityAndFallsBackToDefault()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var settings = new AppSettings { VirtualDesktopOverlayFontColor = "#123456" };
        settings.VirtualDesktopFontColors[first] = "#ABCDEF";
        settings.VirtualDesktopFontColors[second] = "invalid";
        Assert.AreEqual(System.Windows.Media.Color.FromRgb(0xAB, 0xCD, 0xEF), VirtualDesktopColors.Resolve(settings, first));
        Assert.AreEqual(System.Windows.Media.Color.FromRgb(0x12, 0x34, 0x56), VirtualDesktopColors.Resolve(settings, second));
        Assert.AreEqual(VirtualDesktopColors.Resolve(settings, second), VirtualDesktopColors.Resolve(settings, null));
        settings.VirtualDesktopFontColors.Remove(first);
        Assert.AreEqual(VirtualDesktopColors.Resolve(settings, null), VirtualDesktopColors.Resolve(settings, first));
    }
}

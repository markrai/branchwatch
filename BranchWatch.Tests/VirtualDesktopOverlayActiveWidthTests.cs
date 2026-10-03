using System.Windows;
using System.Windows.Media;

namespace BranchWatch.Tests;

[TestClass]
public sealed class VirtualDesktopOverlayActiveWidthTests
{
    private static readonly Guid IdOm = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid IdIgnite = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid IdBranchWatch = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid IdLobo = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid IdRuView = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid IdPersonal = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private static readonly VirtualDesktopInfo[] AllDesktops =
    [
        new(IdOm, "OM"),
        new(IdIgnite, "Ignite"),
        new(IdBranchWatch, "BranchWatch"),
        new(IdLobo, "Lobo"),
        new(IdRuView, "RuView"),
        new(IdPersonal, "PERSONAL")
    ];

    [TestMethod]
    public void ApplySettings_RootBorderWidth_MatchesActiveWidth_AcrossSwitchSequences()
    {
        Exception? failure = null;
        double? baselineBranchWatch = null;
        double? baselineRuView = null;

        var thread = new Thread(() =>
        {
            try
            {
                var settings = CreateSettings(outline: true, scale: 1.0);
                var window = new VirtualDesktopOverlayWindow();

                void ApplyActive(Guid activeId)
                {
                    var state = VirtualDesktopDisplayStateBuilder.Build(AllDesktops, activeId);
                    window.SetDisplayState(state);
                    window.ApplySettings(settings);
                }

                ApplyActive(IdBranchWatch);
                baselineBranchWatch = window.RootBorder.Width;
                Assert.IsGreaterThan(0, baselineBranchWatch.Value);
                AssertActiveOwnsCalculatedWidth(window);

                var sequences = new Guid[][]
                {
                    [IdBranchWatch, IdOm, IdBranchWatch],
                    [IdBranchWatch, IdRuView, IdBranchWatch],
                    [IdIgnite, IdBranchWatch],
                    [IdPersonal, IdBranchWatch],
                    [IdLobo, IdRuView],
                    [IdIgnite, IdBranchWatch, IdLobo, IdRuView, IdBranchWatch, IdOm, IdBranchWatch, IdPersonal, IdBranchWatch]
                };

                foreach (var sequence in sequences)
                {
                    foreach (var activeId in sequence)
                    {
                        ApplyActive(activeId);
                        AssertActiveOwnsCalculatedWidth(window);

                        if (activeId == IdBranchWatch)
                        {
                            Assert.AreEqual(baselineBranchWatch.Value, window.RootBorder.Width, 0.001);
                        }

                        if (activeId == IdRuView)
                        {
                            baselineRuView ??= window.RootBorder.Width;
                            Assert.AreEqual(baselineRuView.Value, window.RootBorder.Width, 0.001);
                        }
                    }
                }

                // Unequal neighbor presence must not change active width for the same name.
                ApplyActive(IdOm);
                ApplyActive(IdBranchWatch);
                Assert.AreEqual(baselineBranchWatch.Value, window.RootBorder.Width, 0.001);

                ApplyActive(IdPersonal);
                ApplyActive(IdBranchWatch);
                Assert.AreEqual(baselineBranchWatch.Value, window.RootBorder.Width, 0.001);

                window.Close();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw failure;
        }

        Assert.IsNotNull(baselineBranchWatch);
        Assert.IsNotNull(baselineRuView);
    }

    [TestMethod]
    public void ApplySettings_ActiveWidth_StableAcrossOutlineAndScale()
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                var window = new VirtualDesktopOverlayWindow();
                foreach (var scale in new[] { 0.5, 0.75, 1.0 })
                {
                    foreach (var outline in new[] { true, false })
                    {
                        var settings = CreateSettings(outline, scale);
                        var state = VirtualDesktopDisplayStateBuilder.Build(AllDesktops, IdBranchWatch);
                        window.SetDisplayState(state);
                        window.ApplySettings(settings);
                        var first = window.RootBorder.Width;

                        window.SetDisplayState(VirtualDesktopDisplayStateBuilder.Build(AllDesktops, IdRuView));
                        window.ApplySettings(settings);

                        window.SetDisplayState(state);
                        window.ApplySettings(settings);
                        Assert.AreEqual(first, window.RootBorder.Width, 0.001);
                        AssertActiveOwnsCalculatedWidth(window);
                    }
                }

                window.Close();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw failure;
        }
    }

    private static void AssertActiveOwnsCalculatedWidth(VirtualDesktopOverlayWindow window)
    {
        Assert.IsGreaterThan(0, window.ActiveWidth);
        Assert.AreEqual(window.ActiveWidth, window.RootBorder.Width, 0.001);
        Assert.AreEqual(window.ActiveHeight, window.RootBorder.Height, 0.001);

        var expectedWindowWidth = window.ActiveWidth
            + window.RootBorder.Margin.Left
            + window.RootBorder.Margin.Right
            + window.LeftStrip.Children.Cast<FrameworkElement>().Sum(child => child.Width + child.Margin.Left + child.Margin.Right)
            + window.RightStrip.Children.Cast<FrameworkElement>().Sum(child => child.Width + child.Margin.Left + child.Margin.Right);
        Assert.AreEqual(expectedWindowWidth, window.Width, 0.001);

        if (window.DesktopText.TextTrimming == TextTrimming.None)
        {
            Assert.AreEqual(DependencyProperty.UnsetValue, window.DesktopText.ReadLocalValue(FrameworkElement.MaxWidthProperty));
            Assert.AreEqual(DependencyProperty.UnsetValue, window.DesktopText.ReadLocalValue(FrameworkElement.WidthProperty));
        }
    }

    private static AppSettings CreateSettings(bool outline, double scale) => new()
    {
        VirtualDesktopOverlayVisible = true,
        VirtualDesktopOverlayPositionPreset = "show-on-taskbar",
        VirtualDesktopOverlayShowOutline = outline,
        VirtualDesktopOverlayScale = scale,
        VirtualDesktopOverlayOpacity = 0.85,
        VirtualDesktopOverlayForegroundOpacity = 1.0,
        VirtualDesktopOverlayFontColor = "#FFFFFF"
    };
}

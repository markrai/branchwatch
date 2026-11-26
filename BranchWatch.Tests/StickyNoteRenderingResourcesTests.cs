using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using WpfBorder = System.Windows.Controls.Border;
using WpfScrollBar = System.Windows.Controls.Primitives.ScrollBar;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfStyle = System.Windows.Style;

namespace BranchWatch.Tests;

[TestClass]
[DoNotParallelize]
public sealed class StickyNoteRenderingResourcesTests
{
    [TestMethod]
    public void PaletteBrushes_AreFrozenAndSharedAcrossDispatchers()
    {
        SolidColorBrush? original = null;
        StaTest.Run(() =>
        {
            foreach (var color in StickyNoteAppearance.Palette)
            {
                foreach (var value in new[] { color.Body, color.Header, color.Alarm })
                {
                    var brush = StickyNoteRenderingResources.BrushFor(value);
                    Assert.IsTrue(brush.IsFrozen);
                    Assert.AreSame(brush, StickyNoteRenderingResources.BrushFor(value));
                    Assert.AreEqual((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value)!,
                        brush.Color);
                }
            }

            original = StickyNoteRenderingResources.BrushFor("#29251F");
        });
        StaTest.Run(() =>
        {
            Assert.AreSame(original, StickyNoteRenderingResources.BrushFor("#29251F"));
            Assert.IsTrue(StickyNoteRenderingResources.BrushFor("#33000000").IsFrozen);
        });
    }

    [TestMethod]
    public void ScrollBarStyle_IsSharedWithinDispatcherAndScopedToTextBoxes()
    {
        WpfStyle? firstStyle = null;
        StaTest.Run(() =>
        {
            var first = new WpfTextBox();
            var second = new WpfTextBox();
            StickyNoteRenderingResources.ApplyScrollBarStyle(first);
            StickyNoteRenderingResources.ApplyScrollBarStyle(second);
            firstStyle = (WpfStyle)first.FindResource(typeof(WpfScrollBar));
            Assert.IsTrue(firstStyle.IsSealed);
            Assert.AreSame(firstStyle, second.FindResource(typeof(WpfScrollBar)));
            first.Resources.Clear();
            Assert.AreSame(firstStyle, second.FindResource(typeof(WpfScrollBar)));
            StickyNoteRenderingResources.ApplyScrollBarStyle(first);
            Assert.AreSame(firstStyle, first.FindResource(typeof(WpfScrollBar)));
        });
        StaTest.Run(() =>
        {
            var third = new WpfTextBox();
            StickyNoteRenderingResources.ApplyScrollBarStyle(third);
            Assert.AreNotSame(firstStyle, third.FindResource(typeof(WpfScrollBar)));
        });
    }

    [TestMethod]
    public void AlarmPulse_DismissalReleasesEffectWithoutStoppingOtherAlarm()
    {
        StaTest.Run(() =>
        {
            var first = new WpfBorder { Visibility = Visibility.Collapsed };
            var second = new WpfBorder { Visibility = Visibility.Collapsed };
            var one = new StickyNoteAlarmPulse(first);
            var two = new StickyNoteAlarmPulse(second);
            var yellow = StickyNoteAppearance.ResolveColor("Yellow").Alarm;
            var blue = StickyNoteAppearance.ResolveColor("Blue").Alarm;
            one.Update(false, yellow);
            Assert.IsNull(first.Effect);
            one.Update(true, yellow);
            two.Update(true, yellow);
            Assert.AreSame(first.BorderBrush, second.BorderBrush);
            Assert.AreNotSame(first.Effect, second.Effect);
            var effect = first.Effect;
            one.Update(true, yellow);
            Assert.AreSame(effect, first.Effect);
            one.Update(true, blue);
            Assert.AreEqual(StickyNoteRenderingResources.BrushFor(blue).Color, ((DropShadowEffect)first.Effect).Color);
            Assert.AreEqual(Visibility.Visible, first.Visibility);

            one.Update(false, blue);
            Assert.AreEqual(Visibility.Collapsed, first.Visibility);
            Assert.IsNull(first.Effect);
            Assert.IsNull(first.BorderBrush);
            Assert.IsFalse(DependencyPropertyHelper.GetValueSource(first, UIElement.OpacityProperty).IsAnimated);
            Assert.AreEqual(Visibility.Visible, second.Visibility);
            Assert.IsNotNull(second.Effect);
            Assert.IsTrue(DependencyPropertyHelper.GetValueSource(second, UIElement.OpacityProperty).IsAnimated);

            one.Update(true, blue);
            Assert.IsNotNull(first.Effect);
            Assert.AreEqual(Visibility.Visible, first.Visibility);
            one.Update(false, blue);
            two.Update(false, yellow);
        });
    }
}

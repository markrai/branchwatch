using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace BranchWatch.Tests;

[TestClass]
public sealed class StickyNoteWindowTests
{
    [TestMethod]
    public void Transparency_RestoresSavedLevelAndSupportsChangesAfterHandleCreation()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var monitor = new VirtualDesktopMonitor();
                using var controller = new StickyNotesController(monitor,
                    Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "notes.json"));
                var note = new StickyNote { DesktopId = Guid.NewGuid(), TransparencyLevel = 8 };
                var window = new StickyNoteWindow(note, controller);
                try
                {
                    Assert.IsTrue(window.AllowsTransparency);
                    Assert.IsTrue(window.Topmost);
                    window.ApplyAppearance(new StickyNoteAppearance("Blue", 8));
                    Assert.AreEqual("Blue", note.Color);
                    Assert.AreEqual(System.Windows.Media.Color.FromRgb(0xDD, 0xEE, 0xFF),
                        ((SolidColorBrush)window.NoteSurface.Background).Color);
                    Assert.AreEqual(WindowStyle.None, window.WindowStyle);
                    Assert.AreEqual(ResizeMode.NoResize, window.ResizeMode);
                    Assert.AreEqual(Colors.Transparent, ((SolidColorBrush)window.Background).Color);
                    Assert.AreEqual(0.55, window.NoteSurface.Opacity, 0.0001);
                    Assert.AreEqual(System.Windows.Media.Color.FromRgb(0xB7, 0xD7, 0xF4),
                        ((SolidColorBrush)window.HeaderSurface.Background).Color);
                    AssertSurfaceAlignment(window);
                    Assert.IsTrue(window.Editor.IsReadOnly);
                    Assert.IsFalse(window.Editor.Focusable);
                    Assert.AreNotEqual(IntPtr.Zero, new WindowInteropHelper(window).EnsureHandle());

                    foreach (var level in new[] { 1, 10, 3 })
                    {
                        window.ApplyTransparency(level);
                        Assert.AreEqual(level, note.TransparencyLevel);
                        Assert.AreEqual(StickyNoteTransparency.OpacityForLevel(level),
                            window.NoteSurface.Opacity, 0.0001);
                        Assert.IsTrue(window.Topmost);
                        Assert.AreEqual(level > 1, window.Editor.IsReadOnly);
                        Assert.AreEqual(level == 1, window.Editor.Focusable);
                        Assert.IsTrue(window.OptionsButton.IsEnabled);
                        AssertSurfaceAlignment(window);
                    }
                }
                finally
                {
                    window.Close();
                }
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

    private static void AssertSurfaceAlignment(StickyNoteWindow window)
    {
        foreach (var width in new[] { 220.0, 300.0, 460.0 })
        {
            var size = new System.Windows.Size(width, 260);
            window.NoteSurface.Measure(size);
            window.NoteSurface.Arrange(new Rect(size));

            var header = window.HeaderSurface.TranslatePoint(new System.Windows.Point(), window.NoteSurface);
            var body = window.BodyHost.TranslatePoint(new System.Windows.Point(), window.NoteSurface);

            Assert.IsGreaterThan(0, window.HeaderSurface.ActualWidth);
            Assert.IsGreaterThan(0, window.BodyHost.ActualHeight);
            Assert.AreEqual(header.X, body.X, 0.001);
            Assert.AreEqual(window.HeaderSurface.ActualWidth, window.BodyHost.ActualWidth, 0.001);
            Assert.AreEqual(header.Y + window.HeaderSurface.ActualHeight, body.Y, 0.001);
            Assert.AreEqual(new Thickness(4, 4, 4, 0), window.Header.Margin);
            var grip = window.ShrinkGrip.TranslatePoint(new System.Windows.Point(), window.BodyHost);
            Assert.AreEqual(0, grip.X, 0.001);
            Assert.AreEqual(window.BodyHost.ActualHeight - window.ShrinkGrip.ActualHeight, grip.Y, 0.001);
            Assert.AreEqual(StickyNoteResizeGeometry.GripSize, window.ShrinkGrip.ActualWidth, 0.001);
            Assert.AreEqual(StickyNoteResizeGeometry.GripSize, window.ShrinkGrip.ActualHeight, 0.001);
            Assert.IsTrue(window.ShrinkGrip.IsHitTestVisible);
        }
    }
}

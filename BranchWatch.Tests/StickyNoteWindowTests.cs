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

    [TestMethod]
    public void Collapse_HidesBodyAndRestoresExpandedDimensionsAcrossAppearanceChanges()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var monitor = new VirtualDesktopMonitor();
                using var controller = new StickyNotesController(monitor,
                    Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "notes.json"));
                var note = new StickyNote { DesktopId = Guid.NewGuid(), Text = "Keep this", Width = 250, Height = 190, TransparencyLevel = 8 };
                var window = new StickyNoteWindow(note, controller);
                try
                {
                    var left = window.Left;
                    var top = window.Top;
                    var width = window.Width;
                    window.SetCollapsed(true);
                    Assert.IsTrue(note.IsCollapsed);
                    Assert.AreEqual(Visibility.Collapsed, window.BodyHost.Visibility);
                    Assert.AreEqual(38.0, window.Height);
                    Assert.AreEqual(190.0, note.Height);
                    Assert.IsFalse(window.IsBodyClickThrough);
                    Assert.AreEqual("Expand note", window.CollapseButton.ToolTip);
                    Assert.AreEqual(2, System.Windows.Controls.Grid.GetColumn(window.CollapseButton));
                    Assert.AreEqual(3, System.Windows.Controls.Grid.GetColumn(window.OptionsButton));

                    foreach (var level in new[] { 1, 10, 3 })
                    {
                        window.ApplyTransparency(level);
                        Assert.AreEqual(38.0, window.Height);
                        Assert.IsFalse(window.IsBodyClickThrough);
                    }

                    window.SetCollapsed(false);
                    Assert.IsFalse(note.IsCollapsed);
                    Assert.AreEqual(Visibility.Visible, window.BodyHost.Visibility);
                    Assert.AreEqual(190.0, window.Height);
                    Assert.AreEqual(150.0, window.MinHeight);
                    Assert.AreEqual(width, window.Width);
                    Assert.AreEqual(left, window.Left);
                    Assert.AreEqual(top, window.Top);
                    Assert.AreEqual("Keep this", window.Editor.Text);
                    Assert.AreEqual("Collapse note", window.CollapseButton.ToolTip);
                    Assert.IsTrue(window.Topmost);

                    window.SetCollapsed(true);
                    window.Close();
                    var restored = new StickyNoteWindow(note, controller);
                    try
                    {
                        Assert.AreEqual(38.0, restored.Height);
                        restored.SetCollapsed(false);
                        Assert.AreEqual(190.0, restored.Height);
                        Assert.AreEqual("Keep this", restored.Editor.Text);
                    }
                    finally
                    {
                        restored.Close();
                    }
                }
                finally
                {
                    if (!note.IsCollapsed && window.IsLoaded)
                    {
                        window.Close();
                    }
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

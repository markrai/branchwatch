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
                    Assert.AreEqual(ResizeMode.CanResize, window.ResizeMode);
                    Assert.AreEqual(Colors.Transparent, ((SolidColorBrush)window.Background).Color);
                    Assert.AreEqual(0.55, window.NoteSurface.Opacity, 0.0001);
                    Assert.AreNotEqual(IntPtr.Zero, new WindowInteropHelper(window).EnsureHandle());

                    foreach (var level in new[] { 1, 10, 3 })
                    {
                        window.ApplyTransparency(level);
                        Assert.AreEqual(level, note.TransparencyLevel);
                        Assert.AreEqual(StickyNoteTransparency.OpacityForLevel(level),
                            window.NoteSurface.Opacity, 0.0001);
                        Assert.IsTrue(window.Topmost);
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
}

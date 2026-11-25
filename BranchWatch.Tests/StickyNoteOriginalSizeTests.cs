using System.IO;
using System.Text.Json;

namespace BranchWatch.Tests;

[TestClass]
public sealed class StickyNoteOriginalSizeTests
{
    [TestMethod]
    public void Load_LegacyNotesRecoverDefaultOrLargerExistingDimensions()
    {
        var path = Path.GetTempFileName();
        try
        {
            var desktop = Guid.NewGuid();
            File.WriteAllText(path, JsonSerializer.Serialize(new[]
            {
                new { Id = Guid.NewGuid(), DesktopId = desktop, Width = 220, Height = 150 },
                new { Id = Guid.NewGuid(), DesktopId = desktop, Width = 420, Height = 350 }
            }));

            var notes = new StickyNoteStore(path).Load();
            Assert.AreEqual<double?>(300, notes[0].OriginalWidth);
            Assert.AreEqual<double?>(260, notes[0].OriginalHeight);
            Assert.AreEqual(220.0, notes[0].Width);
            Assert.AreEqual(150.0, notes[0].Height);
            Assert.AreEqual<double?>(420, notes[1].OriginalWidth);
            Assert.AreEqual<double?>(350, notes[1].OriginalHeight);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void SaveAndLoad_PreservesOriginalSizeAfterShrinking()
    {
        var path = Path.GetTempFileName();
        try
        {
            var note = new StickyNote { DesktopId = Guid.NewGuid(), Width = 420, Height = 350 };
            note.EnsureOriginalSize();
            note.Width = 220;
            note.Height = 150;

            var store = new StickyNoteStore(path);
            store.Save([note]);
            var restored = store.Load()[0];

            Assert.AreEqual<double?>(420, restored.OriginalWidth);
            Assert.AreEqual<double?>(350, restored.OriginalHeight);
            Assert.AreEqual(220.0, restored.Width);
            Assert.AreEqual(150.0, restored.Height);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

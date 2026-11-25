using System.IO;
using System.Text.Json;

namespace BranchWatch.Tests;

[TestClass]
public sealed class StickyNoteStoreTests
{
    [TestMethod]
    public void SaveAndLoad_PreservesIndependentNotesAndDesktopAssignments()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "notes.json");
        try
        {
            var first = new StickyNote { DesktopId = Guid.NewGuid(), Text = "first\nline", Color = "Blue", Left = -250, Top = 80, Width = 420, Height = 300 };
            var second = new StickyNote { DesktopId = Guid.NewGuid(), Text = "second", IsOpen = false };
            var store = new StickyNoteStore(path);
            store.Save([first, second]);

            var notes = store.Load();
            Assert.HasCount(2, notes);
            Assert.AreEqual(first.Id, notes[0].Id);
            Assert.AreEqual(first.DesktopId, notes[0].DesktopId);
            Assert.AreEqual(first.Text, notes[0].Text);
            Assert.AreEqual(first.Color, notes[0].Color);
            Assert.AreEqual(first.Left, notes[0].Left);
            Assert.AreEqual(first.Top, notes[0].Top);
            Assert.AreEqual(first.Width, notes[0].Width);
            Assert.AreEqual(first.Height, notes[0].Height);
            Assert.IsTrue(notes[0].IsOpen);
            Assert.IsFalse(notes[1].IsOpen);
            Assert.AreEqual(second.DesktopId, notes[1].DesktopId);

            store.Save([second]);
            Assert.HasCount(1, store.Load());
            Assert.HasCount(1, Directory.GetFiles(Path.GetDirectoryName(path)!));
        }
        finally
        {
            if (Directory.Exists(Path.GetDirectoryName(path)))
            {
                Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
            }
        }
    }

    [TestMethod]
    public void ForDesktop_UsesIdentityAndOpenState()
    {
        var desktop = Guid.NewGuid();
        var first = new StickyNote { DesktopId = desktop };
        var second = new StickyNote { DesktopId = desktop };
        var closed = new StickyNote { DesktopId = desktop, IsOpen = false };
        var other = new StickyNote { DesktopId = Guid.NewGuid() };
        var notes = new[] { first, second, closed, other };

        CollectionAssert.AreEqual(new[] { first, second }, StickyNote.ForDesktop(notes, desktop).ToArray());
        Assert.IsEmpty(StickyNote.ForDesktop(notes, Guid.NewGuid()).ToArray());
    }

    [TestMethod]
    public void Load_RejectsCorruptFileWithoutReplacingIt()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "{");
            var store = new StickyNoteStore(path);
            Assert.ThrowsExactly<JsonException>(() => store.Load());
            Assert.AreEqual("{", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Load_RejectsDuplicateIds()
    {
        var path = Path.GetTempFileName();
        try
        {
            var note = new StickyNote { DesktopId = Guid.NewGuid() };
            var store = new StickyNoteStore(path);
            store.Save([note, note]);
            Assert.ThrowsExactly<InvalidDataException>(() => store.Load());
        }
        finally
        {
            File.Delete(path);
        }
    }
}

using System.IO;

namespace BranchWatch.Tests;

[TestClass]
public sealed class StickyNotePreferencesTests
{
    [TestMethod]
    public void Defaults_RoundTripAndNormalize()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var service = new SettingsService(Path.Combine(directory, "settings.json"));
            var settings = service.Load();
            Assert.AreEqual(new StickyNoteAppearance("Yellow", 3), StickyNoteAppearance.FromSettings(settings));

            new StickyNoteAppearance("blue", 9).SaveDefaults(service, settings);
            Assert.AreEqual(new StickyNoteAppearance("Blue", 9), StickyNoteAppearance.FromSettings(service.Load()));

            new StickyNoteAppearance("unknown", 30).SaveDefaults(service, settings);
            Assert.AreEqual(new StickyNoteAppearance("Yellow", 10), StickyNoteAppearance.FromSettings(service.Load()));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [TestMethod]
    public void ApplyAll_UpdatesStoredNotesWithoutChangingIdentityContentGeometryOrOpenState()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var storePath = Path.Combine(directory, "notes.json");
                var store = new StickyNoteStore(storePath);
                var service = new SettingsService(Path.Combine(directory, "settings.json"));
                var settings = service.Load();
                var first = new StickyNote
                {
                    DesktopId = Guid.NewGuid(), Text = "first", Color = "Green",
                    TransparencyLevel = 2, Left = -100, Top = 50, Width = 400, Height = 300
                };
                var second = new StickyNote
                {
                    DesktopId = Guid.NewGuid(), Text = "second", IsOpen = false,
                    Color = "Pink", TransparencyLevel = 10
                };
                store.Save([first, second]);

                using var monitor = new VirtualDesktopMonitor();
                using var controller = new StickyNotesController(monitor, storePath, settings);
                controller.Start();

                var appearance = new StickyNoteAppearance("Purple", 7);
                appearance.SaveDefaults(service, settings);
                var newNote = controller.CreateDefaultNote();
                Assert.AreEqual("Purple", newNote.Color);
                Assert.AreEqual(7, newNote.TransparencyLevel);
                Assert.AreEqual("Green", controller.Notes[0].Color);
                Assert.AreEqual("Pink", store.Load()[1].Color);

                Assert.IsTrue(controller.ApplyAppearanceToAll(appearance));
                var saved = store.Load();
                Assert.HasCount(2, saved);
                var originals = new[] { first, second };
                for (var i = 0; i < saved.Count; i++)
                {
                    Assert.AreEqual("Purple", saved[i].Color);
                    Assert.AreEqual(7, saved[i].TransparencyLevel);
                    Assert.AreEqual(originals[i].Id, saved[i].Id);
                    Assert.AreEqual(originals[i].DesktopId, saved[i].DesktopId);
                    Assert.AreEqual(originals[i].Text, saved[i].Text);
                    Assert.AreEqual(originals[i].IsOpen, saved[i].IsOpen);
                    Assert.AreEqual(originals[i].Left, saved[i].Left);
                    Assert.AreEqual(originals[i].Top, saved[i].Top);
                    Assert.AreEqual(originals[i].Width, saved[i].Width);
                    Assert.AreEqual(originals[i].Height, saved[i].Height);
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        try
        {
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure is not null)
            {
                throw failure;
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
